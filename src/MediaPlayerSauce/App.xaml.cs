// Start-up: one copy at a time (a second start hands its files to the first, as Explorer's "Open with" does), the
// person's settings (App Hub's Media Player settings brought across once), and --snapshot for pictures of the window
// drawn off the screen (tests and checks: nothing saved, no sound, nothing on the desktop).
using System.IO;
using System.IO.Pipes;
using System.Security.Principal;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using MediaPlayerSauce.Core;
using MediaPlayerSauce.UI;

namespace MediaPlayerSauce
{
    public partial class App : Application
    {
        Mutex single;
        MainWindow main;
        static string PipeName => "MediaPlayerSE-" + (WindowsIdentity.GetCurrent().User?.Value ?? Environment.UserName);

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            var args = e.Args.ToList();
            string snapshot = Take(args, "--snapshot");
            string scene = Take(args, "--scene") ?? "Welcome";
            string settingsFile = Take(args, "--settings");
            bool enqueue = args.Remove("--enqueue");
            string online = Take(args, "--online-search");
            string source = Take(args, "--source");
            bool showOnline = args.Remove("--online") || online != null;

            if (args.Remove("--register-open-with")) { Shutdown(OpenWith.Register(args.Remove("--all-users")) ? 0 : 1); return; }
            if (args.Remove("--unregister-open-with")) { Shutdown(OpenWith.Unregister(args.Remove("--all-users")) ? 0 : 1); return; }

            if (snapshot != null) { RunSnapshot(snapshot, scene, settingsFile); return; }

            single = new Mutex(true, "Local\\" + PipeName, out bool first);
            if (!first && SendToFirst(args, enqueue)) { Shutdown(0); return; }
            // (the first copy didn't answer, closing or hung: this one carries on)

            DispatcherUnhandledException += OnUiError;
            AppDomain.CurrentDomain.UnhandledException += (s, ev) => Log("crash", ev.ExceptionObject as Exception);

            var settings = Settings.Load(Path.Combine(Settings.DefaultFolder(), "settings.json"));
            if (!settings.ImportedOld && !settings.Blocked)
            {
                OldSettings.ImportInto(settings, OldSettings.DefaultFile());
                settings.ImportedOld = true;
                try { settings.Save(); } catch (Exception ex) { Log("settings", ex); }
            }
            try { main = new MainWindow(settings, false); }
            catch (Exception ex)
            {
                Log("start", ex);
                MessageBox.Show("Media Player couldn't start: " + ex.Message, "Media Player", MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown(1);
                return;
            }
            MainWindow = main;
            main.Show();
            StartPipeServer();
            var files = args.Where(a => !a.StartsWith("--")).ToList();
            if (files.Count > 0) main.OpenPaths(files, !enqueue);
            if (showOnline) main.ShowOnline(source, online);
        }

        static string Take(List<string> args, string name)
        {
            int i = args.FindIndex(a => a.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (i < 0) return null;
            string v = i + 1 < args.Count ? args[i + 1] : "";
            args.RemoveAt(i);
            if (i < args.Count) args.RemoveAt(i);
            return v;
        }

        static string Full(string p)
        {
            if (MediaFiles.IsUrl(p)) return p;
            try { return Path.GetFullPath(p); } catch (Exception) { return p; }
        }

        // ------------------------------------------------------------ one copy at a time
        static bool SendToFirst(List<string> args, bool enqueue)
        {
            try
            {
                using var c = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
                c.Connect(3000);
                var lines = new List<string> { enqueue ? "enqueue" : "play" };
                lines.AddRange(args.Where(a => !a.StartsWith("--")).Select(Full));
                byte[] data = Encoding.UTF8.GetBytes(string.Join("\n", lines));
                c.Write(data, 0, data.Length);
                c.Flush();
                return true;
            }
            catch (Exception) { return false; }
        }

        void StartPipeServer()
        {
            var t = new Thread(() =>
            {
                while (true)
                {
                    try
                    {
                        using var s = new NamedPipeServerStream(PipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.None);
                        s.WaitForConnection();
                        using var ms = new MemoryStream();
                        s.CopyTo(ms);
                        var lines = Encoding.UTF8.GetString(ms.ToArray()).Split('\n', StringSplitOptions.RemoveEmptyEntries).ToList();
                        if (lines.Count == 0) continue;
                        bool enq = lines[0] == "enqueue";
                        var files = lines.Skip(1).ToArray();
                        // only text crosses threads: the window does the rest on its own thread
                        Dispatcher.BeginInvoke(new Action(() => main?.HandleArgs(files, enq)));
                    }
                    catch (Exception) { Thread.Sleep(500); }
                }
            }) { IsBackground = true, Name = "Open requests" };
            t.Start();
        }

        // ------------------------------------------------------------ errors
        void OnUiError(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            Log("ui", e.Exception);
            e.Handled = true;
            try { main?.ShowNotice("Something went wrong (" + e.Exception.Message + "). Media Player carried on; the details are in errors.log."); } catch (Exception) { }
        }

        public static void Log(string where, Exception ex)
        {
            try
            {
                string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MediaPlayerSE");
                Directory.CreateDirectory(dir);
                string file = Path.Combine(dir, "errors.log");
                if (File.Exists(file) && new FileInfo(file).Length > 512 * 1024) File.Delete(file);
                File.AppendAllText(file, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{where}] {ex}\r\n\r\n");
            }
            catch (Exception) { }
        }

        // ------------------------------------------------------------ pictures of the window, off the screen
        void RunSnapshot(string png, string scene, string settingsFile)
        {
            try
            {
                string dir = Path.Combine(Path.GetTempPath(), "MediaPlayerSE-test");
                Directory.CreateDirectory(dir);
                var settings = Settings.Load(settingsFile ?? Path.Combine(dir, "snapshot-settings.json"));
                settings.WinLeft = double.NaN; settings.WinWidth = 1180; settings.WinHeight = 720; settings.WinMax = false;
                main = new MainWindow(settings, true)
                {
                    WindowStartupLocation = WindowStartupLocation.Manual, ShowActivated = false, ShowInTaskbar = false,
                };
                main.Left = -32000; main.Top = -32000;
                MainWindow = main;
                main.Show();
                main.SetupScene(scene);
                var frame = new DispatcherFrame();
                var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(700) };
                timer.Tick += (s, e) => { timer.Stop(); frame.Continue = false; };
                timer.Start();
                Dispatcher.PushFrame(frame);
                main.SaveSnapshot(png);
                Console.Out.WriteLine("snapshot: " + png);
                main.Close();
                Shutdown(0);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("snapshot failed: " + ex);
                Shutdown(1);
            }
        }
    }
}
