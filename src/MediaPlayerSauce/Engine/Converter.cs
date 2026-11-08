// Saving a copy of a file in another format ("Convert / save a copy"). The conversion runs in its OWN libVLC instance
// with dummy sound and picture outputs, so it never disturbs what the main player is playing.
//
// libVLC raises its events on its own threads and calling libVLC from one of them can deadlock, so every event is
// handed to post (the window's Dispatcher, or the tests' queue) first, and Stop runs on the thread pool.
//
// The copy is written to a hidden-ish temporary name next to the destination and moved into place only once libVLC
// has finished and closed it, so a cancelled or failed conversion never leaves half a file behind, and the source is
// never written to.
//
// Only encoders and muxers that ship in VideoLAN.LibVLC.Windows 3.0.24 are offered (each checked end to end by the
// tests' "convert" group): libavcodec's MPEG-4 video, AAC and MP3, Theora, Vorbis and FLAC. The only H.264 encoder
// there makes 10-bit video many TVs and phones can't play, and there is no Matroska / WebM muxer, so no WebM.
// The raw FLAC muxer can't go back to fill in the length, so FlacLength.Fix writes it once the file is complete.
using System.Globalization;
using System.IO;
using LibVLCSharp.Shared;

namespace MediaPlayerSauce.Engine
{
    public sealed class ConvertProfile
    {
        public string Name;          // what the person sees
        public string Ext;           // ".mp4"
        public string Transcode;     // the inside of #transcode{...}
        public string Mux;           // std{mux=...}
        public bool Video;           // keeps the picture
        public override string ToString() { return Name; }

        public static readonly ConvertProfile[] All =
        {
            new ConvertProfile { Name = "Video: MP4 (plays almost everywhere)", Ext = ".mp4", Video = true, Mux = "mp4",
                Transcode = "vcodec=mp4v,venc=avcodec,vb=2500,acodec=mp4a,aenc=avcodec,ab=192,channels=2,samplerate=44100" },
            new ConvertProfile { Name = "Video: OGV (Theora, open format)", Ext = ".ogv", Video = true, Mux = "ogg",
                Transcode = "vcodec=theo,vb=2500,acodec=vorb,ab=192,channels=2,samplerate=44100" },
            new ConvertProfile { Name = "Sound only: MP3 (plays everywhere)", Ext = ".mp3", Video = false, Mux = "raw",
                Transcode = "acodec=mp3,aenc=avcodec,ab=192,channels=2,samplerate=44100" },
            new ConvertProfile { Name = "Sound only: M4A (AAC, smaller than MP3 for the same sound)", Ext = ".m4a", Video = false, Mux = "mp4",
                Transcode = "acodec=mp4a,aenc=avcodec,ab=192,channels=2,samplerate=44100" },
            new ConvertProfile { Name = "Sound only: FLAC (no quality lost)", Ext = ".flac", Video = false, Mux = "raw",
                Transcode = "acodec=flac,channels=2" },
            new ConvertProfile { Name = "Sound only: OGG Vorbis", Ext = ".ogg", Video = false, Mux = "ogg",
                Transcode = "acodec=vorb,ab=192,channels=2,samplerate=44100" },
        };
    }

    public sealed class Converter : IDisposable
    {
        readonly Action<Action> post;
        LibVLC lib;
        MediaPlayer mp;
        Media media;
        string part, dest;
        long lengthMs;
        // 0 idle, 1 running, 2 finishing / stopping, 3 over
        int state;
        bool cancelled, disposed;

        public event Action<double> Progress;            // 0..1
        public event Action<string> Done;                // the finished file
        public event Action<string> Failed;              // a sentence for the person
        public event Action Cancelled;

        public bool Running => state == 1 || state == 2;
        public string Destination => dest;
        /// <summary>The temporary file being written (tests).</summary>
        public string PartFile => part;

        public Converter(Action<Action> post)
        {
            this.post = post;
            Player.InitCore();
        }

        void Post(Action a) { post(() => { if (!disposed) a(); }); }

        /// <summary>The sout chain for a profile writing to dst.</summary>
        public static string Sout(ConvertProfile p, string dst)
        {
            // inside double quotes libVLC unescapes \\ \" \' only, so double every backslash
            string q = dst.Replace("\\", "\\\\").Replace("\"", "\\\"");
            return "#transcode{" + p.Transcode + "}:std{access=file,mux=" + p.Mux + ",dst=\"" + q + "\"}";
        }

        /// <summary>"name.mp4" if free, else "name (2).mp4", "name (3).mp4"...</summary>
        public static string FreeName(string path)
        {
            if (!File.Exists(path) && !Directory.Exists(path)) return path;
            string dir = Path.GetDirectoryName(path) ?? "", name = Path.GetFileNameWithoutExtension(path), ext = Path.GetExtension(path);
            for (int i = 2; i < 10000; i++)
            {
                string c = Path.Combine(dir, name + " (" + i + ")" + ext);
                if (!File.Exists(c) && !Directory.Exists(c)) return c;
            }
            return Path.Combine(dir, name + " " + Guid.NewGuid().ToString("N").Substring(0, 6) + ext);
        }

        /// <summary>A sensible place for the copy: next to a local source, else in Videos or Music. Never an existing file.</summary>
        public static string DefaultDestination(string source, ConvertProfile p)
        {
            string name = "Converted";
            string dir = null;
            try
            {
                if (!string.IsNullOrEmpty(source) && !Core.MediaFiles.IsUrl(source))
                {
                    string full = Path.GetFullPath(source);
                    name = Path.GetFileNameWithoutExtension(full);
                    dir = Path.GetDirectoryName(full);
                    if (dir != null && !Directory.Exists(dir)) dir = null;
                }
                else if (!string.IsNullOrEmpty(source))
                {
                    var u = new Uri(source);
                    string last = Path.GetFileNameWithoutExtension(Uri.UnescapeDataString(u.AbsolutePath.TrimEnd('/')));
                    name = string.IsNullOrWhiteSpace(last) ? u.Host : last;
                }
            }
            catch (Exception) { }
            foreach (char c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
            if (string.IsNullOrWhiteSpace(name)) name = "Converted";
            if (dir == null)
                dir = Environment.GetFolderPath(p.Video ? Environment.SpecialFolder.MyVideos : Environment.SpecialFolder.MyMusic);
            if (string.IsNullOrEmpty(dir)) dir = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            return FreeName(Path.Combine(dir, name + p.Ext));
        }

        public static bool SamePath(string a, string b)
        {
            try { return string.Equals(Path.GetFullPath(a).TrimEnd('\\'), Path.GetFullPath(b).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase); }
            catch (Exception) { return false; }
        }

        /// <summary>Starts converting. The destination is replaced only when the copy is complete (the caller asks the
        /// person first if it exists). Throws ArgumentException with a sentence for the person when it can't start.</summary>
        public void Start(string source, string destination, ConvertProfile profile)
        {
            if (Running) throw new InvalidOperationException("A conversion is already running.");
            if (string.IsNullOrWhiteSpace(source)) throw new ArgumentException("Choose a file to convert first.");
            if (string.IsNullOrWhiteSpace(destination)) throw new ArgumentException("Choose where to save the copy.");
            bool url = Core.MediaFiles.IsUrl(source);
            if (!url && !File.Exists(source)) throw new ArgumentException("The file to convert can't be found.");
            destination = Path.GetFullPath(destination);
            if (!url && SamePath(source, destination)) throw new ArgumentException("The copy can't replace the file it's made from. Choose another name.");
            string dir = Path.GetDirectoryName(destination);
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) throw new ArgumentException("The folder to save in can't be found.");

            dest = destination;
            part = Path.Combine(dir, "~" + Path.GetFileNameWithoutExtension(destination) + "." + Guid.NewGuid().ToString("N").Substring(0, 8) + ".partial" + profile.Ext);
            cancelled = false;
            lengthMs = 0;
            state = 1;

            lib ??= new LibVLC(false, "--quiet", "--no-video-title-show", "--aout=adummy", "--vout=vdummy", "--no-media-library",
                "--no-snapshot-preview", "--no-osd", "--no-sub-autodetect-file");
            mp = new MediaPlayer(lib) { EnableKeyInput = false, EnableMouseInput = false };
            var me = mp;
            me.PositionChanged += (s, e) => { float p = e.Position; Post(() => { if (mp == me && state == 1) Progress?.Invoke(Math.Clamp(p, 0f, 1f)); }); };
            me.LengthChanged += (s, e) => { long l = e.Length; Post(() => { if (l > 0) lengthMs = l; }); };
            me.EndReached += (s, e) => Post(() => { if (mp == me) Finish(true, null); });
            me.EncounteredError += (s, e) =>
            {
                string why = "";
                try { why = lib.LastLibVLCError; } catch (Exception) { }
                Post(() => { if (mp == me) Finish(false, string.IsNullOrWhiteSpace(why) ? "The copy couldn't be made. The file may be damaged or in a form that can't be converted."
                                                                                     : "The copy couldn't be made (" + why + ")."); });
            };

            var opts = new List<string> { ":sout=" + Sout(profile, part), ":no-sout-all", ":sout-keep", ":no-sout-spu" };
            if (!profile.Video) opts.Add(":no-sout-video");
            media = url ? new Media(lib, source, FromType.FromLocation, opts.ToArray())
                        : new Media(lib, Path.GetFullPath(source), FromType.FromPath, opts.ToArray());
            mp.Play(media);
        }

        /// <summary>Stops and removes the partial file; Cancelled follows.</summary>
        public void Cancel()
        {
            if (state != 1) return;
            cancelled = true;
            Finish(false, null);
        }

        // Runs on the window's thread. Stops libVLC on the thread pool (Stop waits for libVLC's threads), then moves the
        // finished file into place or removes the partial one, and reports back through post.
        void Finish(bool ok, string error)
        {
            if (state != 1) return;
            state = 2;
            var oldMp = mp; var oldMedia = media; string p = part, d = dest; bool wasCancelled = cancelled;
            mp = null; media = null;
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try { oldMp.Stop(); } catch (Exception) { }
                try { oldMp.Dispose(); } catch (Exception) { }
                try { oldMedia?.Dispose(); } catch (Exception) { }
                string result = null, failure = error;
                if (ok && !wasCancelled)
                {
                    try
                    {
                        var fi = new FileInfo(p);
                        if (!fi.Exists || fi.Length == 0) failure = "The copy came out empty. This file may not have the kind of picture or sound this format needs.";
                        else
                        {
                            if (p.EndsWith(".flac", StringComparison.OrdinalIgnoreCase)) FlacLength.Fix(p);
                            File.Move(p, d, true); result = d;
                        }
                    }
                    catch (Exception ex) { failure = "The copy was made but couldn't be saved as \"" + Path.GetFileName(d) + "\": " + ex.Message; }
                }
                if (result == null) DeleteQuietly(p);
                post(() =>
                {
                    state = 3;
                    if (disposed) return;
                    if (wasCancelled) Cancelled?.Invoke();
                    else if (result != null) { Progress?.Invoke(1); Done?.Invoke(result); }
                    else Failed?.Invoke(failure ?? "The copy couldn't be made.");
                });
            });
        }

        static void DeleteQuietly(string f)
        {
            for (int i = 0; i < 20; i++)
            {
                try { if (File.Exists(f)) File.Delete(f); return; }
                catch (Exception) { Thread.Sleep(50); }
            }
        }

        public void Dispose()
        {
            if (state == 1) { cancelled = true; Finish(false, null); }
            var l = lib; lib = null;
            // the LibVLC instance goes once any stopping has finished
            if (l != null) ThreadPool.QueueUserWorkItem(_ => { for (int i = 0; i < 200 && state == 2; i++) Thread.Sleep(25); try { l.Dispose(); } catch (Exception) { } });
            disposed = true;
        }

        public static string Percent(double share) { return (share * 100).ToString("0", CultureInfo.InvariantCulture) + "%"; }
    }

    /// <summary>Fills in the total number of samples in a FLAC file's STREAMINFO when the encoder left it at 0 (libVLC's
    /// raw muxer writes the header before it knows the length). The count comes from the last frame's header.</summary>
    public static class FlacLength
    {
        public static bool Fix(string file)
        {
            try
            {
                using var fs = new FileStream(file, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
                var head = new byte[42];
                if (fs.Read(head, 0, 42) < 42 || head[0] != (byte)'f' || head[1] != (byte)'L' || head[2] != (byte)'a' || head[3] != (byte)'C') return false;
                if ((head[4] & 0x7F) != 0) return false;                         // the first block must be STREAMINFO
                long total = ((long)(head[21] & 0x0F) << 32) | ((long)head[22] << 24) | ((long)head[23] << 16) | ((long)head[24] << 8) | head[25];
                if (total != 0) return false;
                int minBlock = (head[8] << 8) | head[9], maxBlock = (head[10] << 8) | head[11];
                long samples = LastFrameEnd(fs, minBlock == maxBlock ? maxBlock : 0);
                if (samples <= 0 || samples >= (1L << 36)) return false;
                head[21] = (byte)((head[21] & 0xF0) | (int)(samples >> 32));
                head[22] = (byte)(samples >> 24); head[23] = (byte)(samples >> 16); head[24] = (byte)(samples >> 8); head[25] = (byte)samples;
                fs.Position = 21;
                fs.Write(head, 21, 5);
                fs.Flush(true);
                return true;
            }
            catch (Exception) { return false; }
        }

        // Looks backwards from the end for the last frame header whose CRC-8 is right; returns the sample just after it.
        static long LastFrameEnd(FileStream fs, int fixedBlock)
        {
            int n = (int)Math.Min(fs.Length, 256 * 1024);
            var b = new byte[n];
            fs.Position = fs.Length - n;
            int got = 0;
            while (got < n) { int r = fs.Read(b, got, n - got); if (r <= 0) break; got += r; }
            for (int i = got - 16; i >= 0; i--)
            {
                if (b[i] != 0xFF || (b[i + 1] & 0xFE) != 0xF8) continue;
                long end = ParseHeader(b, i, got, fixedBlock);
                if (end > 0) return end;
            }
            return -1;
        }

        static long ParseHeader(byte[] b, int at, int len, int fixedBlock)
        {
            bool variable = (b[at + 1] & 1) != 0;
            int bsCode = b[at + 2] >> 4, srCode = b[at + 2] & 15, ch = b[at + 3] >> 4, ss = (b[at + 3] >> 1) & 7;
            if (bsCode == 0 || srCode == 15 || ch > 10 || ss == 3 || ss == 7 || (b[at + 3] & 1) != 0) return -1;
            int p = at + 4;
            // the frame or sample number, UTF-8 style (up to 7 bytes)
            int first = b[p++];
            int more; long num;
            if (first < 0x80) { more = 0; num = first; }
            else if ((first & 0xE0) == 0xC0) { more = 1; num = first & 0x1F; }
            else if ((first & 0xF0) == 0xE0) { more = 2; num = first & 0x0F; }
            else if ((first & 0xF8) == 0xF0) { more = 3; num = first & 0x07; }
            else if ((first & 0xFC) == 0xF8) { more = 4; num = first & 0x03; }
            else if ((first & 0xFE) == 0xFC) { more = 5; num = first & 0x01; }
            else if (first == 0xFE) { more = 6; num = 0; }
            else return -1;
            for (int k = 0; k < more; k++)
            {
                if (p >= len || (b[p] & 0xC0) != 0x80) return -1;
                num = (num << 6) | (long)(b[p++] & 0x3F);
            }
            int block;
            if (bsCode == 1) block = 192;
            else if (bsCode <= 5) block = 576 << (bsCode - 2);
            else if (bsCode == 6) { if (p >= len) return -1; block = b[p++] + 1; }
            else if (bsCode == 7) { if (p + 1 >= len) return -1; block = ((b[p] << 8) | b[p + 1]) + 1; p += 2; }
            else block = 256 << (bsCode - 8);
            if (srCode == 12) p += 1; else if (srCode == 13 || srCode == 14) p += 2;
            if (p >= len) return -1;
            byte crc = 0;
            for (int k = at; k < p; k++)
            {
                crc ^= b[k];
                for (int bit = 0; bit < 8; bit++) crc = (byte)((crc & 0x80) != 0 ? (crc << 1) ^ 0x07 : crc << 1);
            }
            if (crc != b[p]) return -1;
            if (variable) return num + block;
            if (fixedBlock <= 0) return -1;
            return num * fixedBlock + block;
        }
    }
}
