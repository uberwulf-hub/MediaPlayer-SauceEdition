// Which files are media, folders to playlists (in natural order: "2" before "10"), subtitles next to a video, names to
// show and times as text.
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;

namespace MediaPlayerSauce.Core
{
    public static class MediaFiles
    {
        public static readonly string[] Video = { ".3g2", ".3gp", ".amv", ".asf", ".avi", ".divx", ".dv", ".f4v", ".flv", ".h264", ".hevc", ".m1v", ".m2t", ".m2ts", ".m2v",
            ".m4v", ".mkv", ".mov", ".mp2v", ".mp4", ".mpe", ".mpeg", ".mpg", ".mpv2", ".mts", ".mxf", ".nsv", ".nuv", ".ogm", ".ogv", ".qt", ".rm", ".rmvb", ".tod", ".ts",
            ".tts", ".vob", ".vro", ".webm", ".wmv", ".wtv", ".y4m" };
        public static readonly string[] Audio = { ".aac", ".ac3", ".aif", ".aifc", ".aiff", ".alac", ".amr", ".ape", ".au", ".caf", ".dts", ".eac3", ".flac", ".m4a", ".m4b",
            ".m4p", ".mid", ".midi", ".mka", ".mod", ".mp1", ".mp2", ".mp3", ".mpa", ".mpc", ".oga", ".ogg", ".opus", ".ra", ".s3m", ".spx", ".tta", ".voc", ".w64",
            ".wav", ".wma", ".wv", ".xm" };
        public static readonly string[] Subtitles = { ".srt", ".vtt", ".ass", ".ssa", ".sub", ".idx", ".smi", ".sami", ".txt", ".mpl", ".usf", ".ttml", ".dfxp" };
        public static readonly string[] Playlists = { ".m3u", ".m3u8", ".pls" };

        public static bool IsVideo(string p) { return Has(Video, p); }
        public static bool IsAudio(string p) { return Has(Audio, p); }
        public static bool IsMedia(string p) { return Has(Video, p) || Has(Audio, p); }
        public static bool IsSubtitle(string p) { return Has(Subtitles, p) && !p.EndsWith(".txt", StringComparison.OrdinalIgnoreCase); }
        static bool Has(string[] list, string p)
        {
            string e = Path.GetExtension(p ?? "").ToLowerInvariant();
            return e.Length > 0 && Array.IndexOf(list, e) >= 0;
        }

        /// <summary>True for "http://...", "rtsp://...", "dvd:///...": anything with a scheme other than a drive letter.</summary>
        public static bool IsUrl(string p)
        {
            if (string.IsNullOrEmpty(p)) return false;
            int i = p.IndexOf("://", StringComparison.Ordinal);
            if (i < 2) return false;
            for (int k = 0; k < i; k++) { char c = p[k]; if (!(char.IsLetterOrDigit(c) || c == '+' || c == '.' || c == '-')) return false; }
            return true;
        }

        public static string DisplayName(string p)
        {
            if (string.IsNullOrEmpty(p)) return "";
            if (IsUrl(p))
            {
                try
                {
                    var u = new Uri(p);
                    if (u.Scheme is "dvd" or "bluray" or "cdda" or "vcd") return u.Scheme.ToUpperInvariant() switch { "DVD" => "DVD", "BLURAY" => "Blu-ray", "CDDA" => "Audio CD", _ => "Video CD" } + " (" + u.LocalPath.Trim('/', '\\') + ")";
                    string last = Uri.UnescapeDataString(u.Segments.LastOrDefault() ?? "").Trim('/');
                    return last.Length > 0 && last.Contains('.') ? last : u.Host + u.AbsolutePath.TrimEnd('/');
                }
                catch (UriFormatException) { return p; }
            }
            return Path.GetFileNameWithoutExtension(p);
        }

        /// <summary>Every media file in a folder and its subfolders (playlists too, when asked), in natural order:
        /// files of a folder first, then each subfolder.</summary>
        public static List<string> ExpandFolder(string folder, bool playlists = false, int maxFiles = 20000)
        {
            var result = new List<string>();
            Walk(folder, result, playlists, maxFiles, 0);
            return result;
        }

        static void Walk(string folder, List<string> into, bool playlists, int max, int depth)
        {
            if (depth > 12 || into.Count >= max) return;
            string[] files, dirs;
            try { files = Directory.GetFiles(folder); dirs = Directory.GetDirectories(folder); }
            catch (Exception) { return; }
            Array.Sort(files, NaturalCompare);
            foreach (var f in files)
            {
                if (into.Count >= max) return;
                try { if ((File.GetAttributes(f) & (FileAttributes.Hidden | FileAttributes.System)) != 0) continue; } catch (Exception) { continue; }
                if (IsMedia(f) || (playlists && Has(Playlists, f))) into.Add(f);
            }
            Array.Sort(dirs, NaturalCompare);
            foreach (var d in dirs)
            {
                try { if ((File.GetAttributes(d) & (FileAttributes.Hidden | FileAttributes.System | FileAttributes.ReparsePoint)) != 0) continue; } catch (Exception) { continue; }
                Walk(d, into, playlists, max, depth + 1);
            }
        }

        /// <summary>The other files of the same kind in a file's folder (songs beside a song, videos beside a video), in
        /// Explorer's order, the file itself included; just the file when its folder can't be read. No subfolders,
        /// no hidden files, at most max.</summary>
        public static List<string> Neighbours(string file, int max = 2000)
        {
            var result = new List<string>();
            string full;
            try { full = Path.GetFullPath(file); } catch (Exception) { return new List<string> { file }; }
            bool audio = IsAudio(full);
            try
            {
                var files = Directory.GetFiles(Path.GetDirectoryName(full));
                Array.Sort(files, NaturalCompare);
                foreach (var f in files)
                {
                    if (audio ? !IsAudio(f) : !IsVideo(f)) continue;
                    if (!f.Equals(full, StringComparison.OrdinalIgnoreCase))
                        try { if ((File.GetAttributes(f) & (FileAttributes.Hidden | FileAttributes.System)) != 0) continue; } catch (Exception) { continue; }
                    result.Add(f);
                }
            }
            catch (Exception) { return new List<string> { full }; }
            if (!result.Any(f => f.Equals(full, StringComparison.OrdinalIgnoreCase))) result.Add(full);
            if (result.Count > max)
            {
                // keep a window around the file
                int i = result.FindIndex(f => f.Equals(full, StringComparison.OrdinalIgnoreCase));
                int start = Math.Clamp(i - max / 2, 0, result.Count - max);
                result = result.GetRange(start, max);
            }
            return result;
        }

        /// <summary>Subtitle files next to a video: same name ("Film.srt") or the name plus a language ("Film.en.srt",
        /// "Film.English.forced.ass"), and any in a "Subs" / "Subtitles" folder beside it. Best match first.</summary>
        public static List<string> FindSubtitles(string video)
        {
            var found = new List<(string Path, int Rank)>();
            if (string.IsNullOrEmpty(video) || IsUrl(video)) return new List<string>();
            string dir, stem;
            try { dir = Path.GetDirectoryName(Path.GetFullPath(video)); stem = Path.GetFileNameWithoutExtension(video); }
            catch (Exception) { return new List<string>(); }
            if (dir == null) return new List<string>();
            void Look(string folder, int bonus, bool anyName)
            {
                string[] files;
                try { files = Directory.GetFiles(folder); } catch (Exception) { return; }
                foreach (var f in files)
                {
                    if (!IsSubtitle(f)) continue;
                    string s = Path.GetFileNameWithoutExtension(f);
                    if (f.EndsWith(".sub", StringComparison.OrdinalIgnoreCase) && File.Exists(Path.ChangeExtension(f, ".idx"))) continue; // VobSub: the .idx is the one to open
                    int rank;
                    if (s.Equals(stem, StringComparison.OrdinalIgnoreCase)) rank = 0;
                    else if (s.StartsWith(stem + ".", StringComparison.OrdinalIgnoreCase)) rank = 1;
                    else if (anyName) rank = 3;
                    else continue;
                    found.Add((f, rank + bonus));
                }
            }
            Look(dir, 0, false);
            foreach (string sub in new[] { "Subs", "Subtitles", "subs", "subtitles", "Sub" })
            {
                string sd = Path.Combine(dir, sub);
                if (Directory.Exists(sd)) { Look(sd, 2, false); Look(Path.Combine(sd, stem), 2, true); }
            }
            return found.OrderBy(f => f.Rank).ThenBy(f => f.Path, Comparer<string>.Create(NaturalCompare)).Select(f => f.Path)
                        .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        [DllImport("shlwapi.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
        static extern int StrCmpLogicalW(string a, string b);
        /// <summary>Explorer's order: numbers compare as numbers.</summary>
        public static int NaturalCompare(string a, string b)
        {
            try { return StrCmpLogicalW(a ?? "", b ?? ""); }
            catch (Exception) { return string.Compare(a, b, StringComparison.OrdinalIgnoreCase); }
        }

        /// <summary>"1:05", "12:34", "1:02:03"; "--:--" for unknown.</summary>
        public static string Time(long ms)
        {
            if (ms < 0) return "--:--";
            long s = ms / 1000;
            return s >= 3600 ? string.Format(CultureInfo.InvariantCulture, "{0}:{1:00}:{2:00}", s / 3600, s / 60 % 60, s % 60)
                             : string.Format(CultureInfo.InvariantCulture, "{0}:{1:00}", s / 60, s % 60);
        }

        /// <summary>Reads "90", "1:30", "1:02:03", "1h2m3s", "2m", "45s" as milliseconds; -1 if it isn't a time.</summary>
        public static long ParseTime(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return -1;
            string t = text.Trim().ToLowerInvariant().Replace(" ", "");
            if (t.IndexOfAny(new[] { 'h', 'm', 's' }) >= 0)
            {
                long total = 0; string num = "";
                foreach (char c in t)
                {
                    if (char.IsDigit(c) || c == '.') { num += c; continue; }
                    if (!double.TryParse(num, NumberStyles.Float, CultureInfo.InvariantCulture, out double v)) return -1;
                    total += c switch { 'h' => (long)(v * 3600000), 'm' => (long)(v * 60000), 's' => (long)(v * 1000), _ => long.MinValue / 4 };
                    if (total < 0) return -1;
                    num = "";
                }
                if (num.Length > 0) return -1;
                return total;
            }
            string[] parts = t.Split(':');
            if (parts.Length > 3) return -1;
            double ms = 0;
            foreach (string p in parts)
            {
                if (!double.TryParse(p, NumberStyles.Float, CultureInfo.InvariantCulture, out double v) || v < 0) return -1;
                ms = ms * 60 + v;
            }
            return (long)Math.Round(ms * 1000);
        }

        /// <summary>The address libVLC opens for a path or URL.</summary>
        public static string ToMrl(string p)
        {
            if (IsUrl(p)) return p;
            return new Uri(Path.GetFullPath(p)).AbsoluteUri;
        }
    }
}
