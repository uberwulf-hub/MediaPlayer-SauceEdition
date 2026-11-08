// Help > Keyboard shortcuts (every entry of AllShortcuts, grouped, with a search box) and Help > About (version,
// libVLC's version, credits and licences).
using System.IO;
using System.Windows;
using System.Windows.Controls;
using MediaPlayerSauce.Core;

namespace MediaPlayerSauce.UI
{
    public partial class MainWindow
    {
        // ------------------------------------------------------------ keyboard shortcuts
        /// <summary>The shortcuts matching a search (keys or words, any case), in their groups' order.</summary>
        public static List<(string Group, List<Shortcut> Items)> FindShortcuts(string search)
        {
            string s = (search ?? "").Trim();
            var words = s.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var groups = new List<(string, List<Shortcut>)>();
            foreach (var sc in AllShortcuts)
            {
                string text = sc.Group + " " + sc.Keys + " " + sc.What;
                if (!words.All(w => text.Contains(w, StringComparison.OrdinalIgnoreCase))) continue;
                int i = groups.FindIndex(g => g.Item1 == sc.Group);
                if (i < 0) { groups.Add((sc.Group, new List<Shortcut>())); i = groups.Count - 1; }
                groups[i].Item2.Add(sc);
            }
            return groups;
        }

        public void ShowShortcuts()
        {
            var w = ToolUi.Dialog(this, "Keyboard shortcuts", 620);
            w.SizeToContent = SizeToContent.Manual;
            w.Height = Math.Min(720, SystemParameters.WorkArea.Height - 60);
            w.ResizeMode = ResizeMode.CanResize;
            w.MinWidth = 420; w.MinHeight = 300;
            var root = new DockPanel { Margin = new Thickness(20) };
            var head = new StackPanel();
            head.Children.Add(ToolUi.Text("Keyboard shortcuts", "H1"));
            var search = new TextBox { Margin = new Thickness(0, 0, 0, 10) };
            var hint = new TextBlock { Text = "Search, for example \"volume\" or \"Ctrl\"", Foreground = (System.Windows.Media.Brush)FindResource("TextFaint"), IsHitTestVisible = false, Margin = new Thickness(9, 6, 0, 0) };
            head.Children.Add(new Grid { Children = { search, hint } });
            DockPanel.SetDock(head, Dock.Top);
            root.Children.Add(head);
            var close = ToolUi.Button("Close", () => w.Close(), true);
            close.IsCancel = true; close.Margin = new Thickness(0);
            var bottom = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0), Children = { close } };
            DockPanel.SetDock(bottom, Dock.Bottom);
            root.Children.Add(bottom);
            var body = new StackPanel();
            root.Children.Add(new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });

            void Fill()
            {
                hint.Visibility = search.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
                body.Children.Clear();
                var found = FindShortcuts(search.Text);
                if (found.Count == 0) { body.Children.Add(ToolUi.Text("No shortcut matches \"" + search.Text.Trim() + "\".", "Dim")); return; }
                foreach (var (group, items) in found)
                {
                    body.Children.Add(ToolUi.Text(group, "H2"));
                    var g = new Grid();
                    g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(170) });
                    g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    int row = 0;
                    foreach (var sc in items)
                    {
                        g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                        var k = new TextBlock { Text = sc.Keys, FontWeight = FontWeights.SemiBold, Foreground = (System.Windows.Media.Brush)FindResource("Accent"), Margin = new Thickness(0, 3, 12, 3), TextWrapping = TextWrapping.Wrap };
                        var t = new TextBlock { Text = sc.What, Margin = new Thickness(0, 3, 0, 3), TextWrapping = TextWrapping.Wrap };
                        Grid.SetRow(k, row); Grid.SetRow(t, row); Grid.SetColumn(t, 1);
                        g.Children.Add(k); g.Children.Add(t);
                        row++;
                    }
                    body.Children.Add(g);
                }
            }
            search.TextChanged += (s, e) => Fill();
            Fill();
            w.Content = root;
            w.Loaded += (s, e) => search.Focus();
            w.ShowDialog();
        }

        // ------------------------------------------------------------ about
        public static string AppVersion => typeof(MainWindow).Assembly.GetName().Version?.ToString(3) ?? "";

        public void ShowAbout()
        {
            var w = ToolUi.Dialog(this, "About Media Player", 540);
            var p = new StackPanel { Margin = new Thickness(20) };
            p.Children.Add(ToolUi.Text("Media Player (Sauce Edition)", "H1", 20, new Thickness(0, 0, 0, 2)));
            p.Children.Add(ToolUi.Text("Version " + AppVersion, "Dim"));
            p.Children.Add(ToolUi.Text("Plays almost anything, thanks to VLC's own playback engine (libVLC) by VideoLAN.", null, 0, new Thickness(0, 12, 0, 0)));
            string vlc = "";
            try { vlc = Player.Lib?.Version ?? ""; } catch (Exception) { }
            if (vlc.Length > 0) p.Children.Add(ToolUi.Text("libVLC version " + vlc, "Dim", 12, new Thickness(0, 4, 0, 0)));

            p.Children.Add(ToolUi.Text("Made with", "H2"));
            void Credit(string name, string what, string url)
            {
                p.Children.Add(ToolUi.Text(name, null, 0, new Thickness(0, 4, 0, 0)));
                p.Children.Add(ToolUi.Text(what, "Dim", 12));
                if (url != null) p.Children.Add(ToolUi.Link(url, url, new Thickness(0, 0, 0, 2)));
            }
            Credit("libVLC", "VideoLAN. GNU Lesser General Public License, version 2.1 or later (LGPL-2.1-or-later).", "https://www.videolan.org/vlc/libvlc.html");
            Credit("LibVLCSharp", "VideoLAN. GNU Lesser General Public License, version 2.1 (LGPL-2.1).", "https://code.videolan.org/videolan/LibVLCSharp");
            Credit(".NET", "Microsoft and the .NET Foundation. MIT License.", "https://dotnet.microsoft.com");
            p.Children.Add(ToolUi.Text("Media Player (Sauce Edition) isn't made by or affiliated with VideoLAN. VLC is a trademark of VideoLAN.", "Dim", 12, new Thickness(0, 12, 0, 0)));

            var note = ToolUi.Text("", "Dim", 12, new Thickness(0, 8, 0, 0));
            string licences = Path.Combine(AppContext.BaseDirectory, "licenses");
            var btnLic = ToolUi.Button("Open the licences folder", () =>
            {
                if (!Directory.Exists(licences)) { note.Text = "The licences folder isn't next to Media Player here. It comes with every released copy."; return; }
                if (!ToolUi.Open(licences)) note.Text = "The licences folder couldn't be opened.";
            }, false, 0);
            var btnSettings = ToolUi.Button("Open the settings folder", () =>
            {
                string dir = Settings.DefaultFolder();
                try { Directory.CreateDirectory(dir); } catch (Exception) { }
                if (!ToolUi.Open(dir)) note.Text = "The settings folder couldn't be opened.";
            }, false, 0);
            p.Children.Add(ToolUi.Row(new Thickness(0, 16, 0, 0), btnLic, btnSettings));
            p.Children.Add(note);

            var close = ToolUi.Button("Close", () => w.Close(), true);
            close.IsCancel = true; close.IsDefault = true; close.Margin = new Thickness(0);
            p.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 18, 0, 0), Children = { close } });
            w.Content = p;
            w.ShowDialog();
        }
    }
}
