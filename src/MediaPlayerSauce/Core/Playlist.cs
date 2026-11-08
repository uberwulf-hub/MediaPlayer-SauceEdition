// The playlist: what's queued, which one plays, loop (off / all / one) and shuffle, and .m3u / .m3u8 / .pls reading
// and .m3u8 writing. Pure logic (no WPF, no libVLC), so the tests check it directly.
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Text;

namespace MediaPlayerSauce.Core
{
    public sealed class PlaylistItem : INotifyPropertyChanged
    {
        string title, duration = "";
        bool playing;
        public PlaylistItem(string path, string title = null)
        {
            Path = path;
            this.title = string.IsNullOrWhiteSpace(title) ? MediaFiles.DisplayName(path) : title;
        }
        public string Path { get; }
        public bool IsUrl => MediaFiles.IsUrl(Path);
        public string Title { get => title; set { title = value; Changed(nameof(Title)); } }
        public string Duration { get => duration; set { duration = value ?? ""; Changed(nameof(Duration)); } }
        public long LengthMs { get; set; }
        public bool IsPlaying { get => playing; set { playing = value; Changed(nameof(IsPlaying)); } }
        public event PropertyChangedEventHandler PropertyChanged;
        void Changed(string n) { PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n)); }
        public override string ToString() { return Title; }
    }

    public enum LoopMode { Off = 0, All = 1, One = 2 }

    public sealed class Playlist
    {
        readonly Random rng;
        List<int> order = new List<int>();   // play order (shuffled or 0..n-1)
        int orderPos = -1;

        public Playlist(int seed = 0) { rng = seed == 0 ? new Random() : new Random(seed); }

        public ObservableCollection<PlaylistItem> Items { get; } = new ObservableCollection<PlaylistItem>();
        public LoopMode Loop { get; set; }
        bool shuffle;
        public bool Shuffle
        {
            get => shuffle;
            set { if (shuffle == value) return; shuffle = value; Reorder(); }
        }
        public int Index { get; private set; } = -1;
        public PlaylistItem Current => Index >= 0 && Index < Items.Count ? Items[Index] : null;
        public int Count => Items.Count;

        public void Clear() { Items.Clear(); order.Clear(); orderPos = -1; SetIndex(-1); }

        /// <summary>Adds items at the end (or after the current one with next = true); returns the first one added.</summary>
        public PlaylistItem Add(IEnumerable<string> paths, bool next = false)
        {
            PlaylistItem first = null;
            int at = next && Index >= 0 ? Index + 1 : Items.Count;
            foreach (string p in paths)
            {
                if (string.IsNullOrWhiteSpace(p)) continue;
                var it = new PlaylistItem(p);
                Items.Insert(at++, it);
                first ??= it;
            }
            if (first != null) { int keep = Index; Reorder(); if (keep >= 0) SetIndex(Items.IndexOf(Current ?? first)); }
            return first;
        }

        public void Remove(PlaylistItem it)
        {
            int i = Items.IndexOf(it);
            if (i < 0) return;
            PlaylistItem cur = Current;
            Items.RemoveAt(i);
            Reorder();
            SetIndex(cur == it ? -1 : Items.IndexOf(cur));
        }

        public void Move(int from, int to)
        {
            if (from < 0 || from >= Items.Count || to < 0 || to >= Items.Count || from == to) return;
            PlaylistItem cur = Current;
            Items.Move(from, to);
            Reorder();
            SetIndex(Items.IndexOf(cur));
        }

        public void Sort(Comparison<PlaylistItem> cmp)
        {
            PlaylistItem cur = Current;
            var list = Items.ToList();
            list.Sort(cmp);
            Items.Clear();
            foreach (var it in list) Items.Add(it);
            Reorder();
            SetIndex(Items.IndexOf(cur));
        }

        /// <summary>Makes item i the current one (it'll play next).</summary>
        public PlaylistItem Select(int i)
        {
            if (i < 0 || i >= Items.Count) return null;
            SetIndex(i);
            orderPos = order.IndexOf(i);
            return Current;
        }

        /// <summary>The item after the current one, following loop and shuffle (null at the end with loop off).
        /// userAsked = the Next button: "loop one" still moves on.</summary>
        public PlaylistItem Next(bool userAsked)
        {
            if (Items.Count == 0) return null;
            if (!userAsked && Loop == LoopMode.One && Current != null) return Current;
            if (orderPos < 0) orderPos = order.IndexOf(Math.Max(0, Index));
            int np = orderPos + 1;
            if (np >= order.Count)
            {
                if (Loop == LoopMode.Off && !userAsked) return null;
                if (Loop == LoopMode.Off && userAsked) return null;
                if (shuffle) { Reorder(); np = 0; if (order.Count > 1 && order[0] == Index) { (order[0], order[1]) = (order[1], order[0]); } }
                else np = 0;
            }
            orderPos = np;
            SetIndex(order[np]);
            return Current;
        }

        public PlaylistItem Previous()
        {
            if (Items.Count == 0) return null;
            if (orderPos < 0) orderPos = order.IndexOf(Math.Max(0, Index));
            int np = orderPos - 1;
            if (np < 0) np = Loop == LoopMode.Off ? 0 : order.Count - 1;
            orderPos = np;
            SetIndex(order[np]);
            return Current;
        }

        void Reorder()
        {
            order = Enumerable.Range(0, Items.Count).ToList();
            if (shuffle)
            {
                for (int i = order.Count - 1; i > 0; i--) { int j = rng.Next(i + 1); (order[i], order[j]) = (order[j], order[i]); }
                // the playing one goes first, so the rest are still all ahead
                int ci = order.IndexOf(Index);
                if (ci > 0) { order.RemoveAt(ci); order.Insert(0, Index); }
            }
            orderPos = order.IndexOf(Index);
        }

        void SetIndex(int i)
        {
            if (Current != null) Current.IsPlaying = false;
            Index = i;
            if (Current != null) Current.IsPlaying = true;
        }

        // ------------------------------------------------------------ playlist files
        public static bool IsPlaylistFile(string path)
        {
            string e = System.IO.Path.GetExtension(path ?? "").ToLowerInvariant();
            return e == ".m3u" || e == ".m3u8" || e == ".pls";
        }

        /// <summary>Reads .m3u / .m3u8 (with #EXTINF titles) or .pls. Relative paths are taken from the list's folder.</summary>
        public static List<(string Path, string Title)> ReadFile(string file)
        {
            var result = new List<(string, string)>();
            string folder = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(file)) ?? "";
            string ext = System.IO.Path.GetExtension(file).ToLowerInvariant();
            byte[] bytes = File.ReadAllBytes(file);
            // .m3u8 is UTF-8; old .m3u files are often in the PC's ANSI code page
            string text = ext == ".m3u" && !LooksUtf8(bytes) ? Encoding.Latin1.GetString(bytes) : new UTF8Encoding(false).GetString(bytes);
            text = text.TrimStart('﻿');
            var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            if (ext == ".pls")
            {
                var files = new SortedDictionary<int, string>();
                var titles = new Dictionary<int, string>();
                foreach (string raw in lines)
                {
                    string l = raw.Trim();
                    int eq = l.IndexOf('=');
                    if (eq < 0) continue;
                    string k = l.Substring(0, eq).Trim(), v = l.Substring(eq + 1).Trim();
                    if (k.StartsWith("File", StringComparison.OrdinalIgnoreCase) && int.TryParse(k.Substring(4), out int n)) files[n] = v;
                    else if (k.StartsWith("Title", StringComparison.OrdinalIgnoreCase) && int.TryParse(k.Substring(5), out n)) titles[n] = v;
                }
                foreach (var kv in files) result.Add((Resolve(folder, kv.Value), titles.TryGetValue(kv.Key, out var t) ? t : null));
                return result;
            }
            string pendingTitle = null;
            foreach (string raw in lines)
            {
                string l = raw.Trim();
                if (l.Length == 0) continue;
                if (l.StartsWith("#EXTINF:", StringComparison.OrdinalIgnoreCase))
                {
                    int comma = l.IndexOf(',');
                    pendingTitle = comma >= 0 ? l.Substring(comma + 1).Trim() : null;
                    continue;
                }
                if (l.StartsWith("#")) continue;
                result.Add((Resolve(folder, l), string.IsNullOrEmpty(pendingTitle) ? null : pendingTitle));
                pendingTitle = null;
            }
            return result;
        }

        static string Resolve(string folder, string entry)
        {
            if (MediaFiles.IsUrl(entry))
            {
                if (entry.StartsWith("file:///", StringComparison.OrdinalIgnoreCase))
                    try { return new Uri(entry).LocalPath; } catch (UriFormatException) { }
                return entry;
            }
            try { return System.IO.Path.IsPathRooted(entry) ? entry : System.IO.Path.GetFullPath(System.IO.Path.Combine(folder, entry)); }
            catch (Exception) { return entry; }
        }

        static bool LooksUtf8(byte[] b)
        {
            try { new UTF8Encoding(false, true).GetString(b); return true; } catch (DecoderFallbackException) { return false; }
        }

        /// <summary>Writes the playlist as .m3u8 (UTF-8, #EXTINF with the length in seconds and the title).</summary>
        public void Save(string file)
        {
            var sb = new StringBuilder("#EXTM3U\r\n");
            foreach (var it in Items)
            {
                long sec = it.LengthMs > 0 ? it.LengthMs / 1000 : -1;
                sb.Append("#EXTINF:").Append(sec).Append(',').Append(it.Title.Replace("\r", " ").Replace("\n", " ")).Append("\r\n");
                sb.Append(it.Path).Append("\r\n");
            }
            AppHubSaves.SafeFile.WriteText(file, sb.ToString(), false);
        }
    }
}
