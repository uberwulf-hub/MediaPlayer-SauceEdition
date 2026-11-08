// --snapshot scenes: the window as it looks in each state, drawn off the screen with made-up media (nothing is
// played, nothing read from the person's files). The picture "playing" is drawn here: a sunset over hills.
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using MediaPlayerSauce.Core;

namespace MediaPlayerSauce.UI
{
    public partial class MainWindow
    {
        [DllImport("kernel32.dll")] static extern bool AttachConsole(int pid);
        static bool consoleAttached;

        public static void AttachParentConsole()
        {
            if (consoleAttached) return;
            consoleAttached = true;
            try { AttachConsole(-1); } catch (Exception) { }
        }

        public void SetupScene(string scene)
        {
            AttachParentConsole();
            string[] parts = scene.Split(':');
            string name = parts[0];
            Settings.Recent = new List<string> { @"C:\Users\Kim\Videos\Holiday at the lake.mp4", @"C:\Users\Kim\Music\Morning Walk.flac", "https://radio.example/jazz", @"D:\Films\The Long Road (2019).mkv" };
            Settings.Positions[@"D:\Films\The Long Road (2019).mkv"] = new ResumePoint { Ms = 3_725_000, Saved = DateTime.UtcNow };
            RefreshRecent();
            if (name == "Welcome") return;

            Welcome.Visibility = Visibility.Collapsed;
            var items = new[] { @"D:\Films\The Long Road (2019).mkv", @"D:\Films\Harbour Lights.mp4", @"D:\Films\Quiet Mountains.mkv", @"D:\Films\Night Train.avi", @"D:\Films\Paper Boats.webm" };
            Playlist.Add(items);
            long[] lens = { 7_384_000, 5_520_000, 6_010_000, 4_870_000, 5_990_000 };
            for (int i = 0; i < Playlist.Count; i++) { Playlist.Items[i].LengthMs = lens[i]; Playlist.Items[i].Duration = MediaFiles.Time(lens[i]); }
            Playlist.Select(0);
            length = lens[0];
            UpdateTimeUi(2_412_000);
            BtnPlay.Content = "\uE769";
            Title = Playlist.Current.Title + " - Media Player";

            if (name == "Music")
            {
                Playlist.Clear();
                Playlist.Add(new[] { @"C:\Users\Kim\Music\Morning Walk.flac", @"C:\Users\Kim\Music\Evening Rain.flac" });
                Playlist.Select(0);
                length = 245_000; UpdateTimeUi(81_000);
                AudioPanel.Visibility = Visibility.Visible;
                LblSong.Text = "Morning Walk"; LblArtist.Text = "The Sauce Band  \u00b7  Sunny Side";
                ImgCover.Source = DemoCover();
                return;
            }
            SnapshotPicture.Source = DemoPicture(1280, 720);
            if (name == "Playlist") ShowPlaylistPanel(true);
            if (name == "Osd") ShowOsd("Volume 120%");
            if (name == "Notice") ShowNotice("\"Broken.mp4\" couldn't be played. The file may be damaged, or the address may not be reachable.");
            if (name == "Fullscreen") SetFullscreen(true);
            if (name == "Minimal") SetMinimal(true);
        }

        public void SaveSnapshot(string png)
        {
            UpdateLayout();
            var src = PresentationSource.FromVisual(this);
            double dpi = src?.CompositionTarget?.TransformToDevice.M11 ?? 1;
            var root = (FrameworkElement)Content;
            int w = (int)Math.Ceiling(root.ActualWidth * dpi), h = (int)Math.Ceiling(root.ActualHeight * dpi);
            var bmp = new RenderTargetBitmap(w, h, 96 * dpi, 96 * dpi, PixelFormats.Pbgra32);
            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                dc.DrawRectangle((Brush)FindResource("Back"), null, new Rect(0, 0, root.ActualWidth, root.ActualHeight));
                dc.DrawRectangle(new VisualBrush(root), null, new Rect(0, 0, root.ActualWidth, root.ActualHeight));
            }
            bmp.Render(dv);
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(bmp));
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(png)));
            using var f = File.Create(png);
            enc.Save(f);
        }

        /// <summary>A sunset over hills (made here, so snapshots never use anyone's picture).</summary>
        static ImageSource DemoPicture(int w, int h)
        {
            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                var sky = new LinearGradientBrush(new GradientStopCollection
                {
                    new GradientStop(Color.FromRgb(0x24, 0x1B, 0x4A), 0), new GradientStop(Color.FromRgb(0xC2, 0x4A, 0x5A), 0.55), new GradientStop(Color.FromRgb(0xFF, 0xB0, 0x5C), 1)
                }, 90);
                dc.DrawRectangle(sky, null, new Rect(0, 0, w, h));
                dc.DrawEllipse(new RadialGradientBrush(Color.FromRgb(0xFF, 0xF0, 0xB0), Color.FromArgb(0, 0xFF, 0xC0, 0x60)), null, new Point(w * 0.62, h * 0.6), w * 0.16, w * 0.16);
                dc.DrawEllipse(new SolidColorBrush(Color.FromRgb(0xFF, 0xE8, 0xA8)), null, new Point(w * 0.62, h * 0.6), w * 0.05, w * 0.05);
                void Hill(double baseY, double amp, double freq, double phase, Color c)
                {
                    var g = new StreamGeometry();
                    using (var ctx = g.Open())
                    {
                        ctx.BeginFigure(new Point(0, h), true, true);
                        for (int x = 0; x <= w; x += 8)
                            ctx.LineTo(new Point(x, baseY + Math.Sin(x * freq + phase) * amp + Math.Sin(x * freq * 2.7 + phase * 1.7) * amp * 0.35), true, true);
                        ctx.LineTo(new Point(w, h), true, true);
                    }
                    g.Freeze();
                    dc.DrawGeometry(new SolidColorBrush(c), null, g);
                }
                Hill(h * 0.66, h * 0.05, 0.004, 1.0, Color.FromRgb(0x6A, 0x2E, 0x4E));
                Hill(h * 0.74, h * 0.06, 0.006, 2.2, Color.FromRgb(0x45, 0x1F, 0x3C));
                Hill(h * 0.84, h * 0.05, 0.009, 0.4, Color.FromRgb(0x24, 0x12, 0x26));
            }
            var bmp = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
            bmp.Render(dv);
            bmp.Freeze();
            return bmp;
        }

        static ImageSource DemoCover()
        {
            const int s = 480;
            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                dc.DrawRectangle(new LinearGradientBrush(Color.FromRgb(0xFF, 0x6A, 0x2B), Color.FromRgb(0x8E, 0x2B, 0x6E), 45), null, new Rect(0, 0, s, s));
                for (int i = 0; i < 6; i++)
                    dc.DrawEllipse(null, new Pen(new SolidColorBrush(Color.FromArgb((byte)(120 - i * 15), 255, 255, 255)), 3), new Point(s * 0.5, s * 0.55), 40 + i * 32, 40 + i * 32);
                var ft = new FormattedText("Sunny Side", System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI Semibold"), 40, Brushes.White, 1.0);
                dc.DrawText(ft, new Point((s - ft.Width) / 2, 40));
            }
            var bmp = new RenderTargetBitmap(s, s, 96, 96, PixelFormats.Pbgra32);
            bmp.Render(dv);
            bmp.Freeze();
            return bmp;
        }
    }
}
