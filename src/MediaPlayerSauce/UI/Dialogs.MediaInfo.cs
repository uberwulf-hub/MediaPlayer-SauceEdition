// The Media information window (Tools > Media information, Ctrl+I): what the file says about itself, what's inside it
// (one block per track) and how playback is going (refreshed every second). Non-modal; follows the playing file.
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using LibVLCSharp.Shared;
using MediaPlayerSauce.Core;
using MediaPlayerSauce.Engine;

namespace MediaPlayerSauce.UI
{
    public sealed class MediaInfoWindow : Window
    {
        readonly MainWindow main;
        readonly Action<int> show;
        readonly DispatcherTimer timer;
        readonly StackPanel general = new StackPanel(), codecs = new StackPanel(), stats = new StackPanel();
        string generalSig, codecSig;

        public MediaInfoWindow(MainWindow main)
        {
            this.main = main;
            Style = FxKit.S("Dialog");
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Owner = main;
            Title = "Media information";
            Width = 580; Height = 540; MinWidth = 420; MinHeight = 340;
            ResizeMode = ResizeMode.CanResizeWithGrip;
            var (host, s) = FxKit.Tabs(new[] { "General", "Codecs", "Statistics" }, i => FxKit.Page(i == 0 ? general : i == 1 ? codecs : stats));
            show = s;
            var root = new DockPanel();
            var close = new Button { Content = "Close", MinWidth = 90, IsCancel = true, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(16, 6, 16, 14) };
            close.Click += (o, e) => Close();
            DockPanel.SetDock(close, Dock.Bottom);
            root.Children.Add(close);
            root.Children.Add(host);
            Content = root;
            show(0);
            Refresh();
            timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            timer.Tick += (o, e) => Refresh();
            Loaded += (o, e) => timer.Start();
            Closed += (o, e) => timer.Stop();
        }

        public void ShowTab(int tab) { show(tab); }

        void Refresh()
        {
            Player p = main.Player;
            bool has = p != null && p.HasMedia;
            RefreshGeneral(p, has);
            RefreshCodecs(p, has);
            RefreshStats(p, has);
        }

        static void Nothing(StackPanel panel)
        {
            panel.Children.Clear();
            panel.Children.Add(new TextBlock { Text = "Nothing is playing", Style = FxKit.S("H2"), Margin = new Thickness(0, 12, 0, 4) });
            panel.Children.Add(FxKit.Dim("Open a file or a stream, and what's known about it shows here."));
        }

        static Grid Table(IEnumerable<(string Label, string Value)> rows)
        {
            var g = FxKit.Form();
            g.ColumnDefinitions[0].Width = new GridLength(150);
            foreach (var (label, value) in rows)
            {
                if (string.IsNullOrWhiteSpace(value)) continue;
                var t = new TextBox { Text = value, IsReadOnly = true, BorderThickness = new Thickness(0), Background = System.Windows.Media.Brushes.Transparent,
                    TextWrapping = TextWrapping.Wrap, Padding = new Thickness(0), Margin = new Thickness(0, 4, 0, 4), VerticalAlignment = VerticalAlignment.Center };
                FxKit.AddRow(g, label, t);
            }
            return g;
        }

        // ------------------------------------------------------------ general
        void RefreshGeneral(Player p, bool has)
        {
            if (!has) { if (generalSig != "") { generalSig = ""; Nothing(general); } return; }
            string M(MetadataType t) { string v = p.Meta(t); return string.IsNullOrWhiteSpace(v) ? null : v.Trim(); }
            var rows = new List<(string, string)>
            {
                ("Title", M(MetadataType.Title) ?? MediaFiles.DisplayName(p.CurrentPath)),
                ("Artist", M(MetadataType.Artist)),
                ("Album", M(MetadataType.Album)),
                ("Album artist", M(MetadataType.AlbumArtist)),
                ("Genre", M(MetadataType.Genre)),
                ("Date", M(MetadataType.Date)),
                ("Track number", JoinTrack(M(MetadataType.TrackNumber), M(MetadataType.TrackTotal))),
                ("Description", M(MetadataType.Description)),
                ("Copyright", M(MetadataType.Copyright)),
                ("Publisher", M(MetadataType.Publisher)),
                ("Encoded by", M(MetadataType.EncodedBy)),
                ("Now playing", M(MetadataType.NowPlaying)),
                ("Length", p.Length > 0 ? MediaFiles.Time(p.Length) : null),
            };
            string path = p.CurrentPath ?? "";
            string sig = path + "\n" + string.Join("\n", rows.Select(r => r.Item2));
            if (sig == generalSig) return;
            generalSig = sig;
            general.Children.Clear();
            general.Children.Add(Table(rows));
            general.Children.Add(FxKit.H2(MediaFiles.IsUrl(path) ? "Address" : "File", 16));
            var box = new TextBox { Text = path, IsReadOnly = true, TextWrapping = TextWrapping.Wrap };
            general.Children.Add(box);
            var copy = FxKit.Btn("Copy", () =>
            {
                try { Clipboard.SetText(path); main.ShowOsd(MediaFiles.IsUrl(path) ? "Address copied" : "File path copied"); }
                catch (Exception) { main.ShowOsd("The clipboard is busy. Try again."); }
            });
            var row = FxKit.Row(copy);
            if (!MediaFiles.IsUrl(path) && File.Exists(path))
                row.Children.Add(FxKit.Btn("Show in folder", () =>
                {
                    try { System.Diagnostics.Process.Start("explorer.exe", "/select,\"" + path + "\""); } catch (Exception) { }
                }));
            row.Margin = new Thickness(0, 8, 0, 0);
            general.Children.Add(row);
        }

        static string JoinTrack(string n, string total)
        {
            if (string.IsNullOrEmpty(n)) return null;
            return string.IsNullOrEmpty(total) ? n : n + " of " + total;
        }

        // ------------------------------------------------------------ codecs
        void RefreshCodecs(Player p, bool has)
        {
            if (!has) { if (codecSig != "") { codecSig = ""; Nothing(codecs); } return; }
            var tracks = p.Tracks();
            var blocks = new List<(string Head, List<(string, string)> Rows)>();
            int nv = 0, na = 0, ns = 0;
            foreach (var t in tracks)
            {
                var rows = new List<(string, string)>();
                string kind;
                switch (t.TrackType)
                {
                    case TrackType.Video: kind = "Video " + (++nv); break;
                    case TrackType.Audio: kind = "Sound " + (++na); break;
                    case TrackType.Text: kind = "Subtitles " + (++ns); break;
                    default: kind = "Other"; break;
                }
                rows.Add(("Kind", t.TrackType switch { TrackType.Video => "Video", TrackType.Audio => "Sound", TrackType.Text => "Subtitles", _ => "Other" }));
                string codec = p.CodecName(t);
                string four = Player.FourCc(t.Codec);
                rows.Add(("Codec", string.IsNullOrEmpty(codec) ? four : (four.Length > 0 && !codec.Contains(four, StringComparison.OrdinalIgnoreCase) ? codec + " (" + four + ")" : codec)));
                if (!string.IsNullOrWhiteSpace(t.Language)) rows.Add(("Language", t.Language));
                if (!string.IsNullOrWhiteSpace(t.Description)) rows.Add(("Description", t.Description));
                if (t.TrackType == TrackType.Video)
                {
                    var v = t.Data.Video;
                    if (v.Width > 0 && v.Height > 0) rows.Add(("Picture size", v.Width + " 00D7 " + v.Height));
                    if (v.FrameRateNum > 0 && v.FrameRateDen > 0) rows.Add(("Frames a second", ((double)v.FrameRateNum / v.FrameRateDen).ToString("0.###", CultureInfo.InvariantCulture)));
                }
                else if (t.TrackType == TrackType.Audio)
                {
                    var a = t.Data.Audio;
                    if (a.Channels > 0) rows.Add(("Channels", a.Channels switch { 1 => "1 (mono)", 2 => "2 (stereo)", 6 => "6 (5.1 surround)", 8 => "8 (7.1 surround)", _ => a.Channels.ToString(CultureInfo.InvariantCulture) }));
                    if (a.Rate > 0) rows.Add(("Sample rate", a.Rate.ToString("N0", CultureInfo.InvariantCulture) + " Hz"));
                }
                else if (t.TrackType == TrackType.Text && !string.IsNullOrWhiteSpace(t.Data.Subtitle.Encoding)) rows.Add(("Text encoding", t.Data.Subtitle.Encoding));
                if (t.Bitrate > 0) rows.Add(("Bitrate", Kbps(t.Bitrate / 1000.0)));
                blocks.Add((kind, rows));
            }
            string sig = string.Join("\n", blocks.Select(b => b.Head + ":" + string.Join("|", b.Rows.Select(r => r.Item1 + "=" + r.Item2))));
            if (sig == codecSig) return;
            codecSig = sig;
            codecs.Children.Clear();
            if (blocks.Count == 0)
            {
                codecs.Children.Add(FxKit.Dim("Nothing is known about what's inside yet. It shows once playback has started.", 12));
                return;
            }
            bool firstBlock = true;
            foreach (var b in blocks)
            {
                codecs.Children.Add(FxKit.H2(b.Head, firstBlock ? 8 : 18));
                codecs.Children.Add(Table(b.Rows));
                firstBlock = false;
            }
        }

        static string Kbps(double kbps)
        {
            return kbps >= 1000 ? (kbps / 1000).ToString("0.0#", CultureInfo.InvariantCulture) + " Mb/s" : kbps.ToString("0", CultureInfo.InvariantCulture) + " kb/s";
        }

        // ------------------------------------------------------------ statistics
        TextBox[] statBoxes;
        static readonly string[] StatNames =
        {
            "Reading speed", "Unpacking speed", "Data read", "Pictures decoded", "Pictures shown", "Pictures lost",
            "Sound blocks decoded", "Sound buffers played", "Sound buffers lost",
        };

        void RefreshStats(Player p, bool has)
        {
            if (!has) { if (statBoxes != null || stats.Children.Count == 0) { statBoxes = null; Nothing(stats); } return; }
            if (statBoxes == null)
            {
                stats.Children.Clear();
                stats.Children.Add(FxKit.Dim("Updated every second while this window is open. Lost pictures or sound mean the computer or the connection couldn't keep up.", 8));
                var g = FxKit.Form();
                g.ColumnDefinitions[0].Width = new GridLength(180);
                g.Margin = new Thickness(0, 8, 0, 0);
                statBoxes = new TextBox[StatNames.Length];
                for (int i = 0; i < StatNames.Length; i++)
                {
                    statBoxes[i] = new TextBox { IsReadOnly = true, BorderThickness = new Thickness(0), Background = System.Windows.Media.Brushes.Transparent, Padding = new Thickness(0) };
                    FxKit.AddRow(g, StatNames[i], statBoxes[i]);
                }
                stats.Children.Add(g);
            }
            var st = p.Stats();
            if (st == null) { foreach (var b in statBoxes) b.Text = "-"; return; }
            var s = st.Value;
            // libVLC gives bitrates in bytes per microsecond: times 8000 is kilobits a second (as VLC itself shows them)
            string[] values =
            {
                Kbps(s.InputBitrate * 8000), Kbps(s.DemuxBitrate * 8000), Bytes(s.ReadBytes),
                N(s.DecodedVideo), N(s.DisplayedPictures), N(s.LostPictures),
                N(s.DecodedAudio), N(s.PlayedAudioBuffers), N(s.LostAudioBuffers),
            };
            for (int i = 0; i < values.Length; i++) if (statBoxes[i].Text != values[i]) statBoxes[i].Text = values[i];
        }

        static string N(long v) { return v.ToString("N0", CultureInfo.InvariantCulture); }
        static string Bytes(long b)
        {
            if (b < 1024) return b + " bytes";
            if (b < 1024 * 1024) return (b / 1024.0).ToString("0.0", CultureInfo.InvariantCulture) + " KB";
            if (b < 1024L * 1024 * 1024) return (b / 1048576.0).ToString("0.0", CultureInfo.InvariantCulture) + " MB";
            return (b / 1073741824.0).ToString("0.00", CultureInfo.InvariantCulture) + " GB";
        }
    }

    public partial class MainWindow
    {
        MediaInfoWindow mediaInfoWindow;

        public void ShowMediaInfo()
        {
            if (mediaInfoWindow == null)
            {
                mediaInfoWindow = new MediaInfoWindow(this);
                mediaInfoWindow.Closed += (s, e) => mediaInfoWindow = null;
                mediaInfoWindow.Show();
                return;
            }
            if (mediaInfoWindow.WindowState == WindowState.Minimized) mediaInfoWindow.WindowState = WindowState.Normal;
            mediaInfoWindow.Activate();
        }
    }
}
