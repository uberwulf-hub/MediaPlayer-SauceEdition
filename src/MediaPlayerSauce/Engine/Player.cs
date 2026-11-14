// The playback engine: one libVLC instance and its MediaPlayer, wrapped so the window never touches libVLC directly.
// libVLC raises its events on its own threads, and calling back into libVLC from one of them can deadlock, so every
// event is handed to the window's Dispatcher (or, in the tests, to a queue the test pumps) before anyone sees it.
//
// Some settings can only be given when libVLC starts (subtitle size, volume levelling, the visualisation, hardware
// decoding): changing one rebuilds the engine (Rebuild) and the window carries on where it was.
using System.IO;
using System.Windows.Threading;
using LibVLCSharp.Shared;
using MediaPlayerSauce.Core;

namespace MediaPlayerSauce.Engine
{
    public sealed class EngineOptions
    {
        public int SubtitleScale = 100;        // percent
        public bool Normalize;                 // volume levelling
        public string Visual = "";             // "", "spectrum", "scope", "vuMeter", "goom", "visual"
        public bool HardwareDecoding = true;
        public bool Headless;                  // tests: no window, no sound device
        public EngineOptions Clone() { return (EngineOptions)MemberwiseClone(); }
    }

    public sealed class TrackChoice
    {
        public int Id;
        public string Name;
        public override string ToString() { return Name; }
    }

    public sealed class Player : IDisposable
    {
        readonly Action<Action> post;
        public LibVLC Lib { get; private set; }
        public MediaPlayer Mp { get; private set; }
        public EngineOptions Options { get; private set; }
        Media media;
        string currentPath;
        Equalizer eq;
        int volume = 80;
        bool muted;

        public event Action Playing, Paused, Stopped, EndReached, Opening;
        public event Action<string> Failed;
        public event Action<long> TimeChanged, LengthChanged;
        public event Action<float> Buffering;
        public event Action TracksChanged, VideoChanged, MetaChanged, ChaptersChanged;
        /// <summary>The engine was rebuilt (a new MediaPlayer): the window must give it to its video view again.</summary>
        public event Action Rebuilt;

        static bool coreReady;
        static volatile bool makeCache;
        /// <summary>Loads libVLC from libvlc\win-x64 next to the program (once).</summary>
        public static void InitCore()
        {
            if (coreReady) return;
            string dir = Path.Combine(AppContext.BaseDirectory, "libvlc", Environment.Is64BitProcess ? "win-x64" : "win-x86");
            if (Directory.Exists(dir)) LibVLCSharp.Shared.Core.Initialize(dir); else LibVLCSharp.Shared.Core.Initialize();
            makeCache = Directory.Exists(dir) && PluginCacheStale(Path.Combine(dir, "plugins"));
            coreReady = true;
        }

        /// <summary>True on the first start after an update (or ever): libVLC's plugin cache has to be made, which opens
        /// all its plugin files and can take half a minute while Windows Defender checks them.</summary>
        public static bool CacheNeedsBuilding { get { InitCore(); return makeCache; } }

        /// <summary>Makes the plugin cache with a throw-away engine, on any thread, so the start-up can show a "getting
        /// ready" window meanwhile and the window's own engine then starts quickly. False if libVLC couldn't start
        /// (the window's engine then reports it as usual).</summary>
        public static bool BuildPluginCache()
        {
            InitCore();
            makeCache = false;
            try
            {
                using var lib = new LibVLC(false, "--quiet", "--no-media-library", "--reset-plugins-cache");
                return true;
            }
            catch (Exception) { return false; }
        }

        /// <summary>
        /// libVLC's plugin cache (plugins\plugins.dat) is missing or older than the plugins (a new version was unzipped
        /// over the old one). Without it libVLC opens all ~320 plugin files at every start, and on a cold start Windows
        /// Defender checks each one (Andrew, 2026-11-09: "20+ seconds"); with it only the few needed are loaded. The
        /// cache is made here, on this PC, rather than shipped: libVLC ignores entries whose file dates don't match.
        /// </summary>
        public static bool PluginCacheStale(string pluginDir)
        {
            try
            {
                if (!Directory.Exists(pluginDir)) return false;
                var cache = new FileInfo(Path.Combine(pluginDir, "plugins.dat"));
                if (!cache.Exists || cache.Length == 0) return true;
                DateTime newest = DateTime.MinValue;
                foreach (var f in Directory.EnumerateFiles(pluginDir, "*.dll", SearchOption.AllDirectories))
                {
                    var t = File.GetLastWriteTimeUtc(f);
                    if (t > newest) newest = t;
                }
                return newest > cache.LastWriteTimeUtc;
            }
            catch (Exception) { return false; }
        }

        /// <summary>post runs an action on the window's thread (Dispatcher.BeginInvoke), or queues it in tests.</summary>
        public Player(EngineOptions options, Action<Action> post)
        {
            this.post = post;
            InitCore();
            Create(options ?? new EngineOptions());
        }

        public static Action<Action> DispatcherPost(Dispatcher d)
        {
            return a => d.BeginInvoke(DispatcherPriority.Normal, a);
        }

        static string[] Args(EngineOptions o)
        {
            var a = new List<string> { "--quiet", "--no-video-title-show", "--no-snapshot-preview", "--no-osd", "--no-media-library",
                "--sub-text-scale=" + Math.Clamp(o.SubtitleScale, 10, 500) };
            a.Add(o.HardwareDecoding ? "--avcodec-hw=any" : "--avcodec-hw=none");
            if (o.Normalize) a.Add("--audio-filter=normvol");
            if (!string.IsNullOrEmpty(o.Visual)) { a.Add("--audio-visual=visual"); if (o.Visual != "visual") a.Add("--effect-list=" + o.Visual); }
            if (o.Headless) { a.Add("--aout=adummy"); a.Add("--vout=vdummy"); a.Add("--no-audio-time-stretch"); }
            return a.ToArray();
        }

        void Create(EngineOptions o)
        {
            Options = o.Clone();
            var args = Args(Options);
            // the first engine of a run writes the plugin cache when it is missing or out of date (a folder that can't
            // be written, such as Program Files, just carries on without it)
            if (makeCache) { makeCache = false; args = args.Append("--reset-plugins-cache").ToArray(); }
            Lib = new LibVLC(false, args);
            Lib.SetUserAgent("Media Player (Sauce Edition)", "MediaPlayerSE/" + typeof(Player).Assembly.GetName().Version?.ToString(3));
            Mp = new MediaPlayer(Lib) { EnableHardwareDecoding = o.HardwareDecoding, EnableKeyInput = false, EnableMouseInput = false };
            Mp.Playing += (s, e) => { state = VLCState.Playing; };
            Mp.Playing += (s, e) => Post(() => { ApplyVolume(); Playing?.Invoke(); });
            Mp.Paused += (s, e) => { state = VLCState.Paused; };
            Mp.Paused += (s, e) => Post(() => Paused?.Invoke());
            Mp.Stopped += (s, e) => { state = VLCState.Stopped; };
            Mp.Stopped += (s, e) => Post(() => Stopped?.Invoke());
            Mp.Opening += (s, e) => { state = VLCState.Opening; };
            Mp.Opening += (s, e) => Post(() => Opening?.Invoke());
            Mp.EndReached += (s, e) => { state = VLCState.Ended; };
            Mp.EndReached += (s, e) => Post(() => EndReached?.Invoke());
            Mp.EncounteredError += (s, e) => { state = VLCState.Error; };
            Mp.EncounteredError += (s, e) => Post(() => Failed?.Invoke(DescribeError()));
            Mp.TimeChanged += (s, e) => { long t = e.Time; Post(() => TimeChanged?.Invoke(t)); };
            Mp.LengthChanged += (s, e) => { long l = e.Length; Post(() => LengthChanged?.Invoke(l)); };
            Mp.Buffering += (s, e) => { float c = e.Cache; Post(() => Buffering?.Invoke(c)); };
            Mp.ESAdded += (s, e) => Post(() => TracksChanged?.Invoke());
            Mp.ESDeleted += (s, e) => Post(() => TracksChanged?.Invoke());
            Mp.ESSelected += (s, e) => Post(() => TracksChanged?.Invoke());
            Mp.Vout += (s, e) => Post(() => VideoChanged?.Invoke());
            Mp.ChapterChanged += (s, e) => Post(() => ChaptersChanged?.Invoke());
            Mp.TitleChanged += (s, e) => Post(() => ChaptersChanged?.Invoke());
            if (eq != null) Mp.SetEqualizer(eq);
        }

        void Post(Action a) { if (!disposed) post(() => { if (!disposed) a(); }); }

        string DescribeError()
        {
            string why = "";
            try { why = Lib.LastLibVLCError; } catch (Exception) { }
            string name = MediaFiles.DisplayName(currentPath);
            return string.IsNullOrWhiteSpace(why) ? $"\"{name}\" couldn't be played. The file may be damaged, or the address may not be reachable."
                                                  : $"\"{name}\" couldn't be played ({why}).";
        }

        // ------------------------------------------------------------ opening and playing
        public string CurrentPath => currentPath;
        public Media CurrentMedia => media;

        /// <summary>Opens a file, folder-less URL or disc address and starts it (at startMs when given). Extra media options
        /// (":video-filter=transform", ":start-time=...") go to this media only.</summary>
        public void Open(string path, long startMs = 0, IEnumerable<string> mediaOptions = null, bool paused = false)
        {
            WaitForStop();
            currentPath = path;
            var old = media;
            var opts = new List<string>();
            if (startMs > 0) opts.Add(":start-time=" + (startMs / 1000.0).ToString(System.Globalization.CultureInfo.InvariantCulture));
            if (paused) opts.Add(":start-paused");
            if (mediaOptions != null) opts.AddRange(mediaOptions);
            media = MediaFiles.IsUrl(path) ? new Media(Lib, path, FromType.FromLocation, opts.ToArray())
                                           : new Media(Lib, Path.GetFullPath(path), FromType.FromPath, opts.ToArray());
            media.MetaChanged += (s, e) => Post(() => MetaChanged?.Invoke());
            media.ParsedChanged += (s, e) => Post(() => MetaChanged?.Invoke());
            media.DurationChanged += (s, e) => { long d = e.Duration; Post(() => LengthChanged?.Invoke(d)); };
            state = VLCState.Opening;
            Mp.Play(media);
            old?.Dispose();
        }

        public bool HasMedia => media != null;
        // The state comes from libVLC's own events (kept here as they arrive). libVLC's is_playing / get_state can crash
        // when asked while it's still opening a file on a busy PC, so they are never called.
        volatile VLCState state = VLCState.NothingSpecial;
        public VLCState State => state;
        public bool IsPlaying => state == VLCState.Playing;
        public bool IsPaused => state == VLCState.Paused;
        public void Play() { if (media == null) return; var st = state; if (st == VLCState.Ended || st == VLCState.Stopped || st == VLCState.Error || st == VLCState.NothingSpecial) { state = VLCState.Opening; Mp.Play(media); } else Mp.SetPause(false); }
        public void Pause() { if (Mp.CanPause) Mp.SetPause(true); }
        public void TogglePause() { if (IsPlaying) Pause(); else Play(); }
        /// <summary>Stops (libVLC's own Stop can wait for the input thread, so it runs off the window's thread).
        /// Opening something new, rebuilding and closing wait for a stop still under way (WaitForStop), so a late stop
        /// never stops the next file and never touches a player that's gone.</summary>
        public void Stop()
        {
            var mp = Mp;
            // (Stop, Open, Rebuild and Dispose all run on the window's thread, and the last three wait for this first)
            var before = pendingStop;
            var t = Task.Run(() => { try { before?.Wait(5000); } catch (Exception) { } if (!disposed) try { mp.Stop(); } catch (Exception) { } });
            lock (stopGate) pendingStop = t;
        }
        public void StopNow() { WaitForStop(); Mp.Stop(); }
        readonly object stopGate = new object();
        Task pendingStop;
        void WaitForStop()
        {
            Task t;
            lock (stopGate) t = pendingStop;
            try { t?.Wait(5000); } catch (Exception) { }
        }

        public long Time { get => Mp.Time; set { if (Mp.IsSeekable) Mp.Time = Math.Max(0, value); } }
        public long Length => Mp.Length > 0 ? Mp.Length : (media?.Duration ?? -1);
        public float Position { get => Mp.Position; set { if (Mp.IsSeekable) Mp.Position = Math.Clamp(value, 0f, 1f); } }
        public bool Seekable => Mp.IsSeekable;
        public void SeekBy(long ms)
        {
            if (!Mp.IsSeekable) return;
            long len = Length, t = Mp.Time + ms;
            if (len > 0) t = Math.Min(t, len - 500);
            Mp.Time = Math.Max(0, t);
        }
        public float Rate { get => Mp.Rate; set => Mp.SetRate(Math.Clamp(value, 0.25f, 4f)); }
        public void NextFrame() { Mp.NextFrame(); }

        // ------------------------------------------------------------ sound
        /// <summary>0..200 (100 = as recorded).</summary>
        public int Volume { get => volume; set { volume = Math.Clamp(value, 0, 200); ApplyVolume(); } }
        public bool Muted { get => muted; set { muted = value; ApplyVolume(); } }
        void ApplyVolume()
        {
            try { Mp.Volume = volume; Mp.Mute = muted; } catch (Exception) { }
        }
        public long AudioDelayMs { get => Mp.AudioDelay / 1000; set => Mp.SetAudioDelay(value * 1000); }
        public AudioOutputChannel Stereo { get => Mp.Channel; set => Mp.SetChannel(value); }

        public List<TrackChoice> AudioDevices()
        {
            var list = new List<TrackChoice>();
            try
            {
                int i = 0;
                foreach (var d in Mp.AudioOutputDeviceEnum) list.Add(new TrackChoice { Id = i++, Name = d.Description ?? d.DeviceIdentifier });
            }
            catch (Exception) { }
            return list;
        }
        public string AudioDeviceId(string description)
        {
            foreach (var d in Mp.AudioOutputDeviceEnum) if (d.Description == description) return d.DeviceIdentifier;
            return null;
        }
        public void SetAudioDevice(string description)
        {
            if (string.IsNullOrEmpty(description)) { Mp.SetOutputDevice(null); return; }
            string id = AudioDeviceId(description);
            if (id != null) Mp.SetOutputDevice(id);
        }

        // equalizer: libVLC's ten bands and its presets
        public static List<string> EqPresets()
        {
            var l = new List<string>();
            using var e = new Equalizer();
            for (uint i = 0; i < e.PresetCount; i++) l.Add(e.PresetName(i));
            return l;
        }
        public static float[] EqPresetBands(int preset, out float preamp)
        {
            using var e = new Equalizer((uint)preset);
            preamp = e.Preamp;
            var b = new float[e.BandCount];
            for (uint i = 0; i < e.BandCount; i++) b[i] = e.Amp(i);
            return b;
        }
        public static float[] EqFrequencies()
        {
            using var e = new Equalizer();
            var f = new float[e.BandCount];
            for (uint i = 0; i < e.BandCount; i++) f[i] = e.BandFrequency(i);
            return f;
        }
        public void SetEqualizer(bool on, float preamp, float[] bands)
        {
            if (!on) { Mp.UnsetEqualizer(); eq?.Dispose(); eq = null; return; }
            eq ??= new Equalizer();
            eq.SetPreamp(Math.Clamp(preamp, -20, 20));
            for (uint i = 0; i < Math.Min(eq.BandCount, (uint)bands.Length); i++) eq.SetAmp(Math.Clamp(bands[i], -20, 20), i);
            Mp.SetEqualizer(eq);
        }

        // ------------------------------------------------------------ tracks
        static List<TrackChoice> Describe(LibVLCSharp.Shared.Structures.TrackDescription[] list)
        {
            var r = new List<TrackChoice>();
            if (list == null) return r;
            foreach (var t in list) r.Add(new TrackChoice { Id = t.Id, Name = string.IsNullOrWhiteSpace(t.Name) ? "Track " + t.Id : t.Name });
            return r;
        }
        public List<TrackChoice> AudioTracks() => Describe(Mp.AudioTrackDescription);
        public List<TrackChoice> VideoTracks() => Describe(Mp.VideoTrackDescription);
        public List<TrackChoice> SubtitleTracks() => Describe(Mp.SpuDescription);
        public int AudioTrack { get => Mp.AudioTrack; set => Mp.SetAudioTrack(value); }
        public int VideoTrack { get => Mp.VideoTrack; set => Mp.SetVideoTrack(value); }
        public int SubtitleTrack { get => Mp.Spu; set => Mp.SetSpu(value); }
        public long SubtitleDelayMs { get => Mp.SpuDelay / 1000; set => Mp.SetSpuDelay(value * 1000); }
        public bool AddSubtitle(string file)
        {
            if (string.IsNullOrEmpty(file)) return false;
            return Mp.AddSlave(MediaSlaveType.Subtitle, new Uri(Path.GetFullPath(file)).AbsoluteUri, true);
        }
        public bool AddAudio(string file)
        {
            return Mp.AddSlave(MediaSlaveType.Audio, new Uri(Path.GetFullPath(file)).AbsoluteUri, true);
        }

        // ------------------------------------------------------------ titles and chapters (DVDs, Blu-ray, MKV chapters)
        public int ChapterCount => Mp.ChapterCount;
        public int Chapter { get => Mp.Chapter; set => Mp.Chapter = value; }
        public List<TrackChoice> Chapters()
        {
            var r = new List<TrackChoice>();
            try
            {
                int i = 0;
                foreach (var c in Mp.FullChapterDescriptions(-1))
                    r.Add(new TrackChoice { Id = i++, Name = (string.IsNullOrWhiteSpace(c.Name) ? "Chapter " + i : c.Name) + "  (" + MediaFiles.Time(c.TimeOffset) + ")" });
            }
            catch (Exception) { }
            if (r.Count == 0) for (int i = 0; i < Mp.ChapterCount; i++) r.Add(new TrackChoice { Id = i, Name = "Chapter " + (i + 1) });
            return r;
        }
        public void NextChapter() { Mp.NextChapter(); }
        public void PreviousChapter() { Mp.PreviousChapter(); }
        public int TitleCount => Mp.TitleCount;
        public int Title { get => Mp.Title; set => Mp.Title = value; }
        public List<TrackChoice> Titles()
        {
            var r = new List<TrackChoice>();
            try
            {
                int i = 0;
                foreach (var t in Mp.TitleDescription)
                {
                    string n = string.IsNullOrWhiteSpace(t.Name) ? "Title " + (i + 1) : t.Name;
                    r.Add(new TrackChoice { Id = i++, Name = n });
                }
            }
            catch (Exception) { }
            return r;
        }
        /// <summary>DVD / Blu-ray menus: 0 activate, 1 up, 2 down, 3 left, 4 right, 5 popup.</summary>
        public void Navigate(uint how) { Mp.Navigate(how); }

        // ------------------------------------------------------------ picture
        public bool HasVideo => Mp.VoutCount > 0;
        public (uint W, uint H) VideoSize()
        {
            uint w = 0, h = 0;
            try { Mp.Size(0, ref w, ref h); } catch (Exception) { }
            return (w, h);
        }
        public string AspectRatio { get => Mp.AspectRatio; set => Mp.AspectRatio = string.IsNullOrEmpty(value) ? null : value; }
        public string Crop { get => Mp.CropGeometry; set => Mp.CropGeometry = string.IsNullOrEmpty(value) ? null : value; }
        public float Zoom { get => Mp.Scale; set => Mp.Scale = value; }
        public void SetDeinterlace(string mode) { Mp.SetDeinterlace(string.IsNullOrEmpty(mode) ? null : mode); }
        /// <summary>Brightness 0..2 (1), contrast 0..2 (1), saturation 0..3 (1), hue -180..180 (0), gamma 0.01..10 (1).</summary>
        public void SetAdjust(bool on, float brightness, float contrast, float saturation, float hue, float gamma)
        {
            Mp.SetAdjustInt(VideoAdjustOption.Enable, on ? 1 : 0);
            if (!on) return;
            Mp.SetAdjustFloat(VideoAdjustOption.Brightness, brightness);
            Mp.SetAdjustFloat(VideoAdjustOption.Contrast, contrast);
            Mp.SetAdjustFloat(VideoAdjustOption.Saturation, saturation);
            Mp.SetAdjustFloat(VideoAdjustOption.Hue, hue);
            Mp.SetAdjustFloat(VideoAdjustOption.Gamma, gamma);
        }
        /// <summary>Saves the current picture (full size) as a PNG; true once libVLC has accepted it.</summary>
        public bool Snapshot(string file) { return Mp.TakeSnapshot(0, file, 0, 0); }

        // ------------------------------------------------------------ information
        public MediaStats? Stats()
        {
            try { return media?.Statistics; } catch (Exception) { return null; }
        }
        public string Meta(MetadataType t)
        {
            try { return media?.Meta(t); } catch (Exception) { return null; }
        }
        public MediaTrack[] Tracks()
        {
            try { return media?.Tracks ?? Array.Empty<MediaTrack>(); } catch (Exception) { return Array.Empty<MediaTrack>(); }
        }
        public string CodecName(MediaTrack t)
        {
            try { return media?.CodecDescription(t.TrackType, t.Codec) ?? FourCc(t.Codec); } catch (Exception) { return FourCc(t.Codec); }
        }
        public static string FourCc(uint c)
        {
            var ch = new[] { (char)(c & 255), (char)((c >> 8) & 255), (char)((c >> 16) & 255), (char)((c >> 24) & 255) };
            return new string(ch).Trim('\0', ' ');
        }

        // ------------------------------------------------------------ rebuilding with other start-up options
        /// <summary>Starts libVLC again with new options and carries on at the same place (paused stays paused).
        /// The window gives the new MediaPlayer to its video view in Rebuilt.</summary>
        public void Rebuild(EngineOptions o, IEnumerable<string> mediaOptions = null)
        {
            string path = currentPath;
            long at = HasMedia ? Math.Max(0, Mp.Time) : 0;
            bool wasPaused = IsPaused;
            float rate = Mp.Rate;
            int aDelay = (int)AudioDelayMs, sDelay = (int)SubtitleDelayMs;
            WaitForStop();
            var oldMp = Mp; var oldLib = Lib; var oldMedia = media;
            media = null;
            oldMp.Stop();
            Create(o);
            Rebuilt?.Invoke();
            oldMp.Dispose(); oldMedia?.Dispose(); oldLib.Dispose();
            if (path != null)
            {
                Open(path, at, mediaOptions, wasPaused);
                Mp.SetRate(rate);
                if (aDelay != 0) AudioDelayMs = aDelay;
                if (sDelay != 0) SubtitleDelayMs = sDelay;
            }
        }

        bool disposed;
        public void Dispose()
        {
            if (disposed) return;
            WaitForStop();
            lock (stopGate) disposed = true;
            try { Mp.Stop(); } catch (Exception) { }
            Mp.Dispose(); media?.Dispose(); eq?.Dispose(); Lib.Dispose();
        }
    }
}
