// Media Player (Sauce Edition) checks: dotnet run --project tests\MediaPlayerSE.Tests -c Release [-- group,group]
// Everything happens in a temporary folder; nothing touches the person's settings, opens a window or plays a sound
// (libVLC runs with its dummy audio and video outputs).
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;

namespace MediaPlayerSE.Tests
{
    static partial class Program
    {
        static int passed, failed;
        static readonly List<string> failures = new List<string>();
        public static string Temp;

        [STAThread]
        static int Main(string[] args)
        {
            Temp = Path.Combine(Path.GetTempPath(), "MediaPlayerSE-test-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(Temp);
            var only = args.Length > 0 ? args[0].Split(',', StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.OrdinalIgnoreCase) : null;
            var groups = new List<(string Name, Action Run)>
            {
                ("files", FileChecks),
                ("settings", SettingsChecks),
                ("playlist", PlaylistChecks),
                ("engine", EngineChecks),
                ("ui", UiChecks),
                ("discs", DiscChecks),
                ("openwith", OpenWithChecks),
                ("online", OnlineChecks),
                ("convert", ConvertChecks),
                ("default", DefaultChecks),
                ("neighbours", NeighbourChecks),
            };
            // the person's real settings must never be touched by a test
            string real = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MediaPlayerSE");
            string Stamp() { try { return Directory.Exists(real) ? string.Join("|", Directory.GetFiles(real).OrderBy(f => f).Select(f => f + "@" + File.GetLastWriteTimeUtc(f).Ticks)) : "none"; } catch (Exception) { return "?"; } }
            string before = Stamp();
            var sw = Stopwatch.StartNew();
            foreach (var g in groups)
            {
                if (only != null && !only.Contains(g.Name)) continue;
                try { g.Run(); }
                catch (Exception ex) { Fail(g.Name + ": the group stopped with " + ex.GetType().Name + ": " + ex.Message + "\n" + ex.StackTrace); }
            }
            Check("the person's real settings folder was left alone", Stamp() == before);
            try { Directory.Delete(Temp, true); } catch (Exception) { }
            Console.WriteLine();
            if (failed == 0) Console.WriteLine($"ALL PASSED ({passed} checks, {sw.Elapsed.TotalSeconds:0.0} s)");
            else
            {
                Console.WriteLine($"FAILED {failed} of {passed + failed} checks:");
                foreach (var f in failures) Console.WriteLine("  " + f);
            }
            return failed == 0 ? 0 : 1;
        }

        public static void Check(string name, bool ok, string detail = "")
        {
            string line = (ok ? "PASS " : "FAIL ") + name + (string.IsNullOrEmpty(detail) ? "" : "  (" + detail + ")");
            Console.WriteLine(line);
            if (ok) passed++; else { failed++; failures.Add(name + (string.IsNullOrEmpty(detail) ? "" : "  (" + detail + ")")); }
        }
        static void Fail(string what) { Check(what, false); }

        // ------------------------------------------------------------ pumping the engine's events
        public static readonly ConcurrentQueue<Action> Posted = new ConcurrentQueue<Action>();
        public static void Pump() { while (Posted.TryDequeue(out var a)) a(); }
        /// <summary>Pumps events until cond is true or the time is up.</summary>
        public static bool WaitFor(Func<bool> cond, int ms)
        {
            var sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < ms)
            {
                Pump();
                if (cond()) return true;
                Thread.Sleep(15);
            }
            Pump();
            return cond();
        }
        public static string Dir(string name)
        {
            string d = Path.Combine(Temp, name);
            Directory.CreateDirectory(d);
            return d;
        }
    }
}
