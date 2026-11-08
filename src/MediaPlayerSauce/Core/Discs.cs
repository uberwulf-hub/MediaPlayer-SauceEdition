// Discs: the PC's disc drives, what kind of disc is in one (or in a folder copied from a disc), and how to play it.
// The libVLC that comes with Media Player has no DVD reader (dvdnav / dvdread), so a DVD plays as the .VOB files of its
// main title set (the group of VTS_nn_1.VOB, VTS_nn_2.VOB... with the most bytes; VTS_nn_0.VOB is a menu) in order:
// no disc menus, and copy-protected DVDs can't be read. Blu-ray (libbluray), audio CDs (cdda) and video CDs (vcd)
// go through libVLC's own addresses, with "#title:chapter" when a start is given.
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace MediaPlayerSauce.Core
{
    public enum DiscKind { Unknown, Dvd, BluRay, AudioCd, VideoCd }

    public sealed class DiscDrive
    {
        public string Root;          // "D:\"
        public string Letter;        // "D:"
        public bool Ready;           // false: no disc (or one Windows can't read yet)
        public string Label = "";
        public DiscKind Kind;
        public bool HasFiles;        // a data disc with files on it (Kind Unknown)

        /// <summary>One line for the list: "D:  DVD video - HOLIDAY_2019".</summary>
        public string Describe()
        {
            string what = !Ready ? "No disc in the drive" :
                Kind switch
                {
                    DiscKind.Dvd => "DVD video",
                    DiscKind.BluRay => "Blu-ray",
                    DiscKind.AudioCd => "Audio CD",
                    DiscKind.VideoCd => "Video CD",
                    _ => HasFiles ? "A disc with files on it" : "A disc Media Player doesn't recognise",
                };
            return Letter + "   " + what + (Ready && !string.IsNullOrWhiteSpace(Label) ? "  -  " + Label : "");
        }
    }

    public sealed class DvdTitleSet
    {
        public int Number;                                  // nn of VTS_nn
        public List<string> Files = new List<string>();     // VTS_nn_1.VOB, VTS_nn_2.VOB... in order
        public long Bytes;
    }

    public static class Discs
    {
        // ------------------------------------------------------------ the drives
        /// <summary>Every optical drive on the PC with what's in it. Never throws.</summary>
        public static List<DiscDrive> Drives()
        {
            var list = new List<DiscDrive>();
            DriveInfo[] all;
            try { all = DriveInfo.GetDrives(); } catch (Exception) { return list; }
            foreach (var d in all)
            {
                try
                {
                    if (d.DriveType != DriveType.CDRom) continue;
                    var dd = new DiscDrive { Root = d.RootDirectory.FullName, Letter = d.Name.TrimEnd('\\') };
                    bool ready = false;
                    try { ready = d.IsReady; } catch (Exception) { }
                    dd.Ready = ready;
                    if (ready)
                    {
                        try { dd.Label = d.VolumeLabel ?? ""; } catch (Exception) { }
                        dd.Kind = Detect(dd.Root, true, out dd.HasFiles);
                    }
                    list.Add(dd);
                }
                catch (Exception) { }
            }
            return list;
        }

        // ------------------------------------------------------------ what kind of disc
        public static DiscKind Detect(string folder) { return Detect(folder, false, out _); }

        /// <summary>What a drive's root or a folder copied from a disc holds. A drive whose files can't be listed at all is
        /// taken as an audio CD (it has no file system).</summary>
        public static DiscKind Detect(string folder, bool isDriveRoot, out bool hasFiles)
        {
            hasFiles = false;
            if (string.IsNullOrEmpty(folder)) return DiscKind.Unknown;
            string name = Path.GetFileName(folder.TrimEnd('\\', '/'));
            if (name.Equals("VIDEO_TS", StringComparison.OrdinalIgnoreCase)) return DiscKind.Dvd;
            if (name.Equals("BDMV", StringComparison.OrdinalIgnoreCase)) return DiscKind.BluRay;
            if (name.Equals("MPEGAV", StringComparison.OrdinalIgnoreCase)) return DiscKind.VideoCd;
            string[] dirs, files;
            try
            {
                dirs = Directory.GetDirectories(folder).Select(d => Path.GetFileName(d)).ToArray();
                files = Directory.GetFiles(folder).Select(f => Path.GetFileName(f)).ToArray();
            }
            catch (Exception) { return isDriveRoot ? DiscKind.AudioCd : DiscKind.Unknown; }
            bool Has(string d) => dirs.Any(x => x.Equals(d, StringComparison.OrdinalIgnoreCase));
            if (Has("VIDEO_TS")) return DiscKind.Dvd;
            if (Has("BDMV")) return DiscKind.BluRay;
            if (Has("MPEGAV") || Has("SVCD") || (Has("MPEG2") && Has("EXT"))) return DiscKind.VideoCd;
            if (files.Any(f => f.EndsWith(".cda", StringComparison.OrdinalIgnoreCase))) return DiscKind.AudioCd;
            hasFiles = files.Length > 0 || dirs.Length > 0;
            if (isDriveRoot && !hasFiles) return DiscKind.AudioCd;
            return DiscKind.Unknown;
        }

        /// <summary>A guess at a disc image's kind from the names in its first 8 MB (ISO 9660 and UDF keep folder names
        /// as plain letters). Unknown when it can't tell.</summary>
        public static DiscKind DetectIso(string iso)
        {
            try
            {
                using var f = new FileStream(iso, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                var buf = new byte[(int)Math.Min(f.Length, 8 * 1024 * 1024)];
                int n = 0, r;
                while (n < buf.Length && (r = f.Read(buf, n, buf.Length - n)) > 0) n += r;
                string s = Encoding.ASCII.GetString(buf, 0, n);
                if (s.Contains("BDMV")) return DiscKind.BluRay;
                if (s.Contains("VIDEO_TS")) return DiscKind.Dvd;
                if (s.Contains("MPEGAV") || s.Contains("SVCD")) return DiscKind.VideoCd;
            }
            catch (Exception) { }
            return DiscKind.Unknown;
        }

        public static bool IsImage(string path)
        {
            string e = Path.GetExtension(path ?? "").ToLowerInvariant();
            return e == ".iso" || e == ".cue" || e == ".bin" || e == ".img";
        }

        // ------------------------------------------------------------ DVDs: the main title set's .VOB files
        static readonly Regex Vob = new Regex(@"^VTS_(\d\d)_(\d+)\.VOB$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        /// <summary>The VIDEO_TS folder for a drive root, a disc folder or the VIDEO_TS folder itself (null when none).</summary>
        public static string VideoTsFolder(string location)
        {
            if (string.IsNullOrEmpty(location)) return null;
            try
            {
                string p = location.TrimEnd('\\', '/');
                if (p.Length == 2 && p[1] == ':') p += "\\";
                if (Path.GetFileName(p).Equals("VIDEO_TS", StringComparison.OrdinalIgnoreCase) && Directory.Exists(p)) return p;
                string v = Path.Combine(p, "VIDEO_TS");
                return Directory.Exists(v) ? v : null;
            }
            catch (Exception) { return null; }
        }

        /// <summary>Every title set with its parts (VTS_nn_1.VOB up, the menu VTS_nn_0.VOB left out), biggest first.</summary>
        public static List<DvdTitleSet> TitleSets(string videoTs)
        {
            var sets = new Dictionary<int, DvdTitleSet>();
            var parts = new Dictionary<int, List<(int Part, string File, long Bytes)>>();
            try
            {
                foreach (var f in Directory.GetFiles(videoTs))
                {
                    var m = Vob.Match(Path.GetFileName(f));
                    if (!m.Success) continue;
                    int set = int.Parse(m.Groups[1].Value), part = int.Parse(m.Groups[2].Value);
                    if (set == 0 || part == 0) continue;
                    long bytes = 0;
                    try { bytes = new FileInfo(f).Length; } catch (Exception) { }
                    if (!parts.TryGetValue(set, out var l)) parts[set] = l = new List<(int, string, long)>();
                    l.Add((part, f, bytes));
                }
            }
            catch (Exception) { }
            foreach (var kv in parts)
            {
                var ts = new DvdTitleSet { Number = kv.Key };
                foreach (var p in kv.Value.OrderBy(p => p.Part)) { ts.Files.Add(p.File); ts.Bytes += p.Bytes; }
                sets[kv.Key] = ts;
            }
            return sets.Values.OrderByDescending(s => s.Bytes).ThenBy(s => s.Number).ToList();
        }

        /// <summary>The main film: the title set with the most bytes (or the one asked for). Null when there is none.</summary>
        public static DvdTitleSet MainTitleSet(string videoTs, int wanted = 0)
        {
            var sets = TitleSets(videoTs);
            if (wanted > 0) return sets.FirstOrDefault(s => s.Number == wanted);
            return sets.FirstOrDefault();
        }

        // ------------------------------------------------------------ libVLC addresses
        /// <summary>libVLC's address for a Blu-ray, audio CD or video CD in a drive, a disc folder or (Blu-ray, video CD) a
        /// disc image; "#title:chapter" is added when a start is given (0 = not given). Null for a DVD (it plays as files)
        /// or a kind that can't be played from there.</summary>
        public static string Address(DiscKind kind, string location, int title = 0, int chapter = 0)
        {
            if (string.IsNullOrWhiteSpace(location)) return null;
            string loc = location.Trim().Trim('"');
            bool drive = IsDrive(loc);
            bool image = !drive && IsImage(loc);
            string scheme;
            switch (kind)
            {
                case DiscKind.BluRay:
                    scheme = "bluray";
                    if (!drive && !image && Path.GetFileName(loc.TrimEnd('\\', '/')).Equals("BDMV", StringComparison.OrdinalIgnoreCase))
                        loc = Path.GetDirectoryName(loc.TrimEnd('\\', '/'));
                    break;
                case DiscKind.AudioCd:
                    if (!drive) return null;
                    scheme = "cdda"; title = chapter = 0;   // the CD's tracks become the playlist
                    break;
                case DiscKind.VideoCd:
                    if (!drive && !image) return null;
                    scheme = "vcd";
                    break;
                default: return null;
            }
            string path = drive ? char.ToUpperInvariant(loc[0]) + ":/" : Encode(Path.GetFullPath(loc).TrimEnd('\\').Replace('\\', '/')) + (image ? "" : "/");
            string addr = scheme + ":///" + path;
            if (title > 0 || chapter > 0) addr += "#" + Math.Max(1, title) + (chapter > 0 ? ":" + chapter : "");
            return addr;
        }

        /// <summary>"D:", "D:\" or "D:/".</summary>
        public static bool IsDrive(string p)
        {
            if (string.IsNullOrEmpty(p)) return false;
            p = p.TrimEnd('\\', '/');
            return p.Length == 2 && p[1] == ':' && char.IsLetter(p[0]);
        }

        // '#' starts libVLC's title:chapter part and '%' starts an escape, so both are escaped in a path (spaces too)
        static string Encode(string p)
        {
            var sb = new StringBuilder();
            foreach (char c in p)
            {
                if (c == '%') sb.Append("%25");
                else if (c == '#') sb.Append("%23");
                else if (c == ' ') sb.Append("%20");
                else sb.Append(c);
            }
            return sb.ToString();
        }

        public static string KindName(DiscKind k)
        {
            return k switch { DiscKind.Dvd => "DVD", DiscKind.BluRay => "Blu-ray", DiscKind.AudioCd => "Audio CD", DiscKind.VideoCd => "Video CD", _ => "disc" };
        }

        public const string ProtectedNote = "Shop-bought DVDs and Blu-rays are copy-protected, and Media Player can't play those. Home-made discs and copies you made yourself play fine.";
        public const string BluRayFailed = "This Blu-ray couldn't be played. Most shop-bought Blu-rays are copy-protected, and Media Player can't play those; home-made Blu-rays play fine.";
    }
}
