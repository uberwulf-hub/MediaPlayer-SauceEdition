// Brings App Hub's Media Player settings across once, on the first start: volume, recent files, where each file was
// left, the last folder, recent YouTube searches and whether the playlist was shown. App Hub's file is only read,
// never changed or moved (App Hub's Media Player keeps working with it).
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace MediaPlayerSauce.Core
{
    public static class OldSettings
    {
        public static string DefaultFile()
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MediaPlayer", "settings.json");
        }

        /// <summary>Copies what App Hub's Media Player remembered into s. Returns true when anything was found.</summary>
        public static bool ImportInto(Settings s, string file)
        {
            if (string.IsNullOrEmpty(file)) return false;
            JsonObject o;
            try
            {
                // read only, straight from the disk: App Hub's file is never set aside, marked or rewritten
                byte[] data = File.Exists(file) ? File.ReadAllBytes(file) : null;
                if (data == null) return false;
                o = JsonNode.Parse(StripBom(data), null, new JsonDocumentOptions { AllowTrailingCommas = true }) as JsonObject;
                if (o == null) return false;
            }
            catch (Exception) { return false; }

            // Volume was 0..1 (1 = as recorded)
            if (o["Volume"] != null) s.Volume = (int)Math.Clamp(Math.Round(Settings.NumOf(o["Volume"], 0.8) * 100), 0, 100);
            if (o["Recent"] is JsonArray rec)
                foreach (var n in rec.Reverse())
                    if (n is JsonValue v && v.TryGetValue(out string p) && !string.IsNullOrWhiteSpace(p)) s.AddRecent(p);
            if (o["YtRecent"] is JsonArray yt)
                foreach (var n in yt)
                    if (n is JsonValue v && v.TryGetValue(out string q) && !string.IsNullOrWhiteSpace(q) && !s.YtRecent.Contains(q, StringComparer.OrdinalIgnoreCase) && s.YtRecent.Count < Settings.MaxYtRecent) s.YtRecent.Add(q);
            if (o["LastFolder"] is JsonValue lf && lf.TryGetValue(out string folder) && string.IsNullOrEmpty(s.LastFolder)) s.LastFolder = folder;
            if (o["ShowPlaylist"] is JsonValue sp && sp.TryGetValue(out bool show)) s.ShowPlaylist = show;
            // Positions were seconds; PositionTimes were UTC ticks
            var times = o["PositionTimes"] as JsonObject;
            if (o["Positions"] is JsonObject pos)
                foreach (var kv in pos)
                {
                    double sec = Settings.NumOf(kv.Value, 0);
                    if (sec <= 0 || s.Positions.ContainsKey(kv.Key)) continue;
                    DateTime when = DateTime.UtcNow.AddDays(-1);
                    if (times != null && times[kv.Key] != null)
                    {
                        long ticks = (long)Settings.NumOf(times[kv.Key], 0);
                        if (ticks > DateTime.MinValue.Ticks && ticks < DateTime.MaxValue.Ticks) when = new DateTime(ticks, DateTimeKind.Utc);
                    }
                    s.Positions[kv.Key] = new ResumePoint { Ms = (long)(sec * 1000), Saved = when };
                }
            // SubSize was a font size in pixels (26 by default)
            if (o["SubSize"] != null) s.SubtitleScale = (int)Math.Clamp(Math.Round(Settings.NumOf(o["SubSize"], 26) / 26.0 * 100), 50, 300);
            return true;
        }

        static byte[] StripBom(byte[] b)
        {
            return b.Length >= 3 && b[0] == 0xEF && b[1] == 0xBB && b[2] == 0xBF ? b.Skip(3).ToArray() : b;
        }
    }
}
