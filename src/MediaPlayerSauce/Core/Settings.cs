// The person's settings: %APPDATA%\MediaPlayerSE\settings.json, saved through SafeFile (a temporary file, five
// backups, an unreadable file set aside and never overwritten). Unknown fields (written by a newer version) are kept
// and written back as they were; missing fields get their defaults; older versions are upgraded in memory and the new
// version is written only after a successful load.
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using AppHubSaves;

namespace MediaPlayerSauce.Core
{
    public sealed class ResumePoint
    {
        public long Ms;
        public DateTime Saved;
    }

    public sealed class EqSettings
    {
        public bool On;
        public int Preset = -1;          // -1 = the person's own bands
        public float Preamp = 12f;
        public float[] Bands = new float[10];
    }

    public sealed class Settings
    {
        public const int Version = 1;
        public const int MaxRecent = 20, MaxResume = 300, MaxYtRecent = 8;

        public int Volume = 80;                  // 0..200 (100 = as recorded)
        public bool Muted;
        public List<string> Recent = new List<string>();
        public Dictionary<string, ResumePoint> Positions = new Dictionary<string, ResumePoint>(StringComparer.OrdinalIgnoreCase);
        public string LastFolder = "";
        public List<string> YtRecent = new List<string>();
        public bool ShowPlaylist;
        public int Loop;                         // 0 off, 1 all, 2 one
        public bool Shuffle;
        public int SubtitleScale = 100;          // percent
        public string AudioDevice = "";
        public bool Resume = true;
        public string OnTop = "never";           // never, always, playing
        public bool PauseOnMinimize;
        public EqSettings Eq = new EqSettings();
        public double WinLeft = double.NaN, WinTop = double.NaN, WinWidth = 1100, WinHeight = 680;
        public bool WinMax;
        public bool ImportedOld;                 // App Hub's Media Player settings were brought across once
        public double UiScale = 1.0;
        public bool Normalize;                   // even out loud and quiet parts
        public string Visual = "";               // music visualisation: "", spectrum, scope, vuMeter, goom, visual
        public bool HardwareDecoding = true;

        /// <summary>Fields this version doesn't know (a newer version wrote them): written back unchanged.</summary>
        public JsonObject Extra = new JsonObject();

        public string File { get; private set; }
        /// <summary>A sentence for the person when the settings had to come from a backup (empty otherwise).</summary>
        public string LoadMessage { get; private set; } = "";
        /// <summary>True when the file exists but couldn't be read at all: nothing is saved over it.</summary>
        public bool Blocked { get; private set; }

        public static string DefaultFolder()
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MediaPlayerSE");
        }

        static readonly string[] Known = { "version", "volume", "muted", "recent", "positions", "lastFolder", "ytRecent", "showPlaylist", "loop", "shuffle",
            "subtitleScale", "audioDevice", "resume", "onTop", "pauseOnMinimize", "eq", "window", "importedOld", "uiScale", "normalize", "visual", "hardwareDecoding" };

        public static Settings Load(string file)
        {
            var s = new Settings { File = file };
            if (!System.IO.File.Exists(file) && SafeFile.Backups(file).Count == 0) return s;
            try
            {
                SaveLoad info;
                JsonObject o = SafeFile.Load(file, b => Parse(b), out info);
                s.Read(o);
                s.LoadMessage = info.Message ?? "";
            }
            catch (FileNotFoundException) { }
            catch (SaveUnreadableException)
            {
                s.Blocked = true;
                s.LoadMessage = "Your settings couldn't be opened, so Media Player is using its defaults this time and won't save over them.";
            }
            return s;
        }

        static JsonObject Parse(byte[] b)
        {
            var node = JsonNode.Parse(b, null, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
            if (node is not JsonObject o) throw new InvalidDataException("Settings must be a JSON object");
            return o;
        }

        void Read(JsonObject o)
        {
            Volume = Math.Clamp(Int(o, "volume", Volume), 0, 200);
            Muted = Bool(o, "muted", Muted);
            Recent = Strings(o, "recent").Take(MaxRecent).ToList();
            if (o["positions"] is JsonObject pos)
                foreach (var kv in pos)
                {
                    if (kv.Value is not JsonObject p) continue;
                    long ms = Long(p, "ms", 0);
                    DateTime at = DateTime.TryParse(Str(p, "saved", ""), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var d) ? d : DateTime.MinValue;
                    if (ms > 0) Positions[kv.Key] = new ResumePoint { Ms = ms, Saved = at };
                }
            LastFolder = Str(o, "lastFolder", LastFolder);
            YtRecent = Strings(o, "ytRecent").Take(MaxYtRecent).ToList();
            ShowPlaylist = Bool(o, "showPlaylist", ShowPlaylist);
            Loop = Math.Clamp(Int(o, "loop", Loop), 0, 2);
            Shuffle = Bool(o, "shuffle", Shuffle);
            SubtitleScale = Math.Clamp(Int(o, "subtitleScale", SubtitleScale), 25, 400);
            AudioDevice = Str(o, "audioDevice", AudioDevice);
            Resume = Bool(o, "resume", Resume);
            OnTop = Str(o, "onTop", OnTop) switch { "always" => "always", "playing" => "playing", _ => "never" };
            PauseOnMinimize = Bool(o, "pauseOnMinimize", PauseOnMinimize);
            if (o["eq"] is JsonObject e)
            {
                Eq.On = Bool(e, "on", false);
                Eq.Preset = Int(e, "preset", -1);
                Eq.Preamp = (float)Dbl(e, "preamp", 12);
                if (e["bands"] is JsonArray ba)
                    for (int i = 0; i < Math.Min(10, ba.Count); i++) Eq.Bands[i] = (float)Math.Clamp(NumOf(ba[i], 0), -20, 20);
            }
            if (o["window"] is JsonObject w)
            {
                WinLeft = Dbl(w, "left", double.NaN); WinTop = Dbl(w, "top", double.NaN);
                WinWidth = Math.Max(480, Dbl(w, "width", WinWidth)); WinHeight = Math.Max(320, Dbl(w, "height", WinHeight));
                WinMax = Bool(w, "max", false);
            }
            ImportedOld = Bool(o, "importedOld", false);
            UiScale = Math.Clamp(Dbl(o, "uiScale", 1.0), 0.75, 2.0);
            Normalize = Bool(o, "normalize", false);
            Visual = Str(o, "visual", "") switch { "spectrum" => "spectrum", "scope" => "scope", "vuMeter" => "vuMeter", "goom" => "goom", "visual" => "visual", _ => "" };
            HardwareDecoding = Bool(o, "hardwareDecoding", true);
            foreach (var kv in o)
                if (Array.IndexOf(Known, kv.Key) < 0) Extra[kv.Key] = kv.Value?.DeepClone();
        }

        public JsonObject ToJson()
        {
            var o = new JsonObject
            {
                ["version"] = Version,
                ["volume"] = Volume,
                ["muted"] = Muted,
                ["recent"] = new JsonArray(Recent.Take(MaxRecent).Select(r => (JsonNode)JsonValue.Create(r)).ToArray()),
            };
            var pos = new JsonObject();
            foreach (var kv in Positions.OrderByDescending(k => k.Value.Saved).Take(MaxResume))
                pos[kv.Key] = new JsonObject { ["ms"] = kv.Value.Ms, ["saved"] = kv.Value.Saved.ToString("o", CultureInfo.InvariantCulture) };
            o["positions"] = pos;
            o["lastFolder"] = LastFolder ?? "";
            o["ytRecent"] = new JsonArray(YtRecent.Take(MaxYtRecent).Select(r => (JsonNode)JsonValue.Create(r)).ToArray());
            o["showPlaylist"] = ShowPlaylist;
            o["loop"] = Loop;
            o["shuffle"] = Shuffle;
            o["subtitleScale"] = SubtitleScale;
            o["audioDevice"] = AudioDevice ?? "";
            o["resume"] = Resume;
            o["onTop"] = OnTop;
            o["pauseOnMinimize"] = PauseOnMinimize;
            o["eq"] = new JsonObject { ["on"] = Eq.On, ["preset"] = Eq.Preset, ["preamp"] = Eq.Preamp, ["bands"] = new JsonArray(Eq.Bands.Select(b => (JsonNode)JsonValue.Create(b)).ToArray()) };
            var win = new JsonObject { ["width"] = WinWidth, ["height"] = WinHeight, ["max"] = WinMax };
            if (!double.IsNaN(WinLeft)) win["left"] = WinLeft;
            if (!double.IsNaN(WinTop)) win["top"] = WinTop;
            o["window"] = win;
            o["importedOld"] = ImportedOld;
            o["uiScale"] = UiScale;
            o["normalize"] = Normalize;
            o["visual"] = Visual;
            o["hardwareDecoding"] = HardwareDecoding;
            foreach (var kv in Extra) if (!o.ContainsKey(kv.Key)) o[kv.Key] = kv.Value?.DeepClone();
            return o;
        }

        /// <summary>Saves (unless the file on the disk couldn't be read: then nothing is written over it).</summary>
        public bool Save()
        {
            if (Blocked || string.IsNullOrEmpty(File)) return false;
            Directory.CreateDirectory(Path.GetDirectoryName(File));
            SafeFile.WriteText(File, ToJson().ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            return true;
        }

        // ------------------------------------------------------------ recent files and resume points
        public void AddRecent(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return;
            Recent.RemoveAll(r => string.Equals(r, path, StringComparison.OrdinalIgnoreCase));
            Recent.Insert(0, path);
            if (Recent.Count > MaxRecent) Recent.RemoveRange(MaxRecent, Recent.Count - MaxRecent);
        }

        /// <summary>Remembers where a file was left (only for files over 3 minutes, and not in the last 10 seconds or the first 10).</summary>
        public void Remember(string path, long ms, long length)
        {
            if (string.IsNullOrEmpty(path)) return;
            if (length < 180000 || ms < 10000 || ms > length - 10000) { Positions.Remove(path); return; }
            Positions[path] = new ResumePoint { Ms = ms, Saved = DateTime.UtcNow };
            if (Positions.Count > MaxResume + 20)
                foreach (var k in Positions.OrderBy(p => p.Value.Saved).Take(Positions.Count - MaxResume).Select(p => p.Key).ToList()) Positions.Remove(k);
        }

        public long ResumeAt(string path)
        {
            return path != null && Positions.TryGetValue(path, out var p) ? p.Ms : 0;
        }

        // ------------------------------------------------------------ JSON helpers
        static int Int(JsonObject o, string k, int d) { return (int)Math.Round(NumOf(o[k], d)); }
        static long Long(JsonObject o, string k, long d) { return (long)Math.Round(NumOf(o[k], d)); }
        static double Dbl(JsonObject o, string k, double d) { return NumOf(o[k], d); }
        internal static double NumOf(JsonNode n, double d)
        {
            if (n is JsonValue v)
            {
                if (v.TryGetValue(out double x) && !double.IsNaN(x) && !double.IsInfinity(x)) return x;
                if (v.TryGetValue(out string s) && double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out x)) return x;
            }
            return d;
        }
        static bool Bool(JsonObject o, string k, bool d)
        {
            if (o[k] is JsonValue v) { if (v.TryGetValue(out bool b)) return b; if (v.TryGetValue(out string s)) return s.Equals("true", StringComparison.OrdinalIgnoreCase); }
            return d;
        }
        static string Str(JsonObject o, string k, string d) { return o[k] is JsonValue v && v.TryGetValue(out string s) ? s : d; }
        static IEnumerable<string> Strings(JsonObject o, string k)
        {
            if (o[k] is JsonArray a)
            {
                foreach (var n in a) if (n is JsonValue v && v.TryGetValue(out string s) && !string.IsNullOrWhiteSpace(s)) yield return s;
            }
            else if (o[k] is JsonValue one && one.TryGetValue(out string single) && !string.IsNullOrWhiteSpace(single)) yield return single;
        }
    }
}
