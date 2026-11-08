// The real window, off the screen, with the silent engine (no sound device, no picture window): opening files, the
// keyboard shortcuts, full screen and back, and a picture of every snapshot scene.
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using MediaPlayerSauce.Core;
using MediaPlayerSauce.UI;

namespace MediaPlayerSE.Tests
{
    static partial class Program
    {
        static MediaPlayerSauce.App app;

        static void EnsureApp()
        {
            if (app != null) return;
            app = new MediaPlayerSauce.App { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            app.InitializeComponent();
        }

        /// <summary>Lets the window's thread run (the engine's events arrive through it) until cond or the time is up.</summary>
        static bool WaitUi(Func<bool> cond, int ms)
        {
            var end = DateTime.UtcNow.AddMilliseconds(ms);
            while (DateTime.UtcNow < end)
            {
                var frame = new DispatcherFrame();
                var t = new DispatcherTimer(TimeSpan.FromMilliseconds(20), DispatcherPriority.Background, (s, e) => frame.Continue = false, Dispatcher.CurrentDispatcher);
                t.Start();
                Dispatcher.PushFrame(frame);
                t.Stop();
                if (cond()) return true;
            }
            return cond();
        }

        static MainWindow NewWindow(string settingsFile)
        {
            EnsureApp();
            var s = Settings.Load(settingsFile);
            var w = new MainWindow(s, true) { WindowStartupLocation = WindowStartupLocation.Manual, ShowActivated = false, ShowInTaskbar = false };
            w.Left = -32000; w.Top = -32000;
            w.Show();
            return w;
        }

        static void UiChecks()
        {
            string d = Dir("ui");
            string avi = Path.Combine(d, "clip.avi"), wav = Path.Combine(d, "song.wav"), settingsFile = Path.Combine(d, "settings.json");
            TestMedia.Avi(avi, 60, 10, 320, 240);
            TestMedia.Wav(wav, 3);

            var w = NewWindow(settingsFile);
            Check("ui: the window opens with the start page", w.Welcome.Visibility == Visibility.Visible && w.Playlist.Count == 0);
            var bad = new List<string>();
            foreach (var dict in app.Resources.MergedDictionaries)
                foreach (var key in dict.Keys) { try { app.FindResource(key); } catch (Exception ex) { bad.Add(key + ": " + ex.GetBaseException().Message); } }
            Check("ui: every style and colour of the theme loads", bad.Count == 0, string.Join("; ", bad));
            string dialogError = null;
            try { new InputBox(w, "t", "p", "", "", "OK", null); MediaPlayerSauce.UI.ToolUi.Dialog(w, "t", 400); }
            catch (Exception ex) { dialogError = ex.GetBaseException().Message; }
            Check("ui: dialogs can be made with the theme's dialog style", dialogError == null, dialogError);
            w.OpenPaths(new[] { avi, wav, Path.Combine(d, "nothing-here.txt") });
            Check("ui: files go into the playlist and the first one plays", w.Playlist.Count == 3 && w.Playlist.Current?.Path == avi && w.Welcome.Visibility != Visibility.Visible);
            Check("ui: it really plays", WaitUi(() => w.Player.IsPlaying && w.Player.Time > 200, 6000), w.Player.State + " " + w.Player.Time);
            Check("ui: the time shows under the picture", WaitUi(() => w.LblTime.Text != "0:00" || w.Player.Time < 1000, 3000) && w.LblLength.Text == "0:06", w.LblTime.Text + " / " + w.LblLength.Text);
            Check("ui: its length goes into the playlist", w.Playlist.Items[0].Duration == "0:06", w.Playlist.Items[0].Duration);

            int vol = w.Player.Volume;
            w.HandleKey(Key.Up, false, false, false);
            Check("ui: Up turns the volume up", w.Player.Volume == vol + 5 && w.LblVol.Text == (vol + 5) + "%");
            w.HandleKey(Key.M, false, false, false);
            Check("ui: M mutes", w.Player.Muted);
            w.HandleKey(Key.M, false, false, false);
            w.HandleKey(Key.OemCloseBrackets, false, false, false);
            Check("ui: ] plays faster", Math.Abs(w.Player.Rate - 1.1f) < 0.01 && (string)w.BtnSpeed.Content == "1.1x", w.Player.Rate.ToString());
            w.HandleKey(Key.OemPlus, false, false, false);
            Check("ui: = goes back to normal speed", Math.Abs(w.Player.Rate - 1f) < 0.01);
            w.HandleKey(Key.L, false, false, false);
            Check("ui: L loops the playlist", w.Playlist.Loop == LoopMode.All && w.BtnLoop.IsChecked == true);
            w.HandleKey(Key.L, false, false, false); w.HandleKey(Key.L, false, false, false);
            Check("ui: and off again after 'this one'", w.Playlist.Loop == LoopMode.Off);
            long before = w.Player.Time;
            w.HandleKey(Key.D5, false, false, false);
            Check("ui: 5 jumps half way", WaitUi(() => w.Player.Time >= 2800, 3000), before + " -> " + w.Player.Time);
            w.HandleKey(Key.Space, false, false, false);
            Check("ui: Space pauses", WaitUi(() => w.Player.IsPaused, 3000));
            w.HandleKey(Key.Space, false, false, false);
            Check("ui: Space plays again", WaitUi(() => w.Player.IsPlaying, 3000));

            w.SetFullscreen(true);
            Check("ui: full screen hides the menu and floats the controls over the picture", w.MenuBar.Visibility == Visibility.Collapsed && w.Controls.Parent == w.Overlay && w.IsFullscreen);
            w.HandleKey(Key.Escape, false, false, false);
            Check("ui: Esc leaves full screen and the controls go back below", !w.IsFullscreen && w.Controls.Parent == w.Root && System.Windows.Controls.Grid.GetRow(w.Controls) == 2 && w.MenuBar.Visibility == Visibility.Visible);

            w.HandleKey(Key.N, false, false, false);
            Check("ui: N plays the next one", WaitUi(() => w.Playlist.Current?.Path == wav && w.Player.IsPlaying, 5000), w.Playlist.Current?.Path);
            Check("ui: music shows its own panel instead of a black picture", WaitUi(() => w.AudioPanel.Visibility == Visibility.Visible, 3000));
            Check("ui: a file that can't play moves on (and says so)", WaitUi(() => w.Notice.Visibility == Visibility.Visible || w.Playlist.Current?.Path != wav, 8000));

            w.ShowPlaylistPanel(true);
            Check("ui: the playlist panel shows its count", w.LblPlCount.Text.StartsWith("3 items"), w.LblPlCount.Text);
            w.Close();
            Check("ui: test windows never save settings", !File.Exists(settingsFile));

            foreach (string scene in new[] { "Welcome", "Playing", "Music", "Playlist", "Osd", "Notice", "Fullscreen", "Minimal" })
            {
                var sw = NewWindow(Path.Combine(d, "scene-settings.json"));
                sw.SetupScene(scene);
                WaitUi(() => false, 150);
                string png = Path.Combine(d, scene + ".png");
                sw.SaveSnapshot(png);
                sw.Close();
                bool ok = File.Exists(png);
                int colours = 0;
                if (ok)
                {
                    var bmp = new FormatConvertedBitmap(BitmapFrame.Create(new Uri(png), BitmapCreateOptions.None, BitmapCacheOption.OnLoad), System.Windows.Media.PixelFormats.Bgra32, null, 0);
                    int stride = bmp.PixelWidth * 4;
                    var px = new byte[stride * bmp.PixelHeight];
                    bmp.CopyPixels(px, stride, 0);
                    var seen = new HashSet<int>();
                    for (int i = 0; i < px.Length; i += 4 * 97) seen.Add(px[i] | px[i + 1] << 8 | px[i + 2] << 16);
                    colours = seen.Count;
                }
                Check("ui: the " + scene + " scene draws", ok && colours > 20, colours + " colours");
            }
        }
    }
}
