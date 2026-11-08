// Preferences (Ctrl+P) and Explorer's "Open with" (Help > Open with). Every change applies at once and is saved
// through SaveSoon. ToolUi holds the small builders the disc, preferences and help windows share.
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using MediaPlayerSauce.Core;

namespace MediaPlayerSauce.UI
{
    static class ToolUi
    {
        public static Window Dialog(Window owner, string title, double width)
        {
            var w = new Window
            {
                Style = (Style)Application.Current.FindResource("Dialog"),
                Owner = owner, Title = title, Width = width,
                SizeToContent = SizeToContent.Height, ResizeMode = ResizeMode.NoResize, WindowStartupLocation = WindowStartupLocation.CenterOwner,
            };
            if (owner is MainWindow mw && mw.Quiet) { w.ShowActivated = false; }
            return w;
        }

        public static TextBlock Text(string text, string style = null, double size = 0, Thickness? margin = null)
        {
            var t = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap };
            if (style != null) t.Style = (Style)Application.Current.FindResource(style);
            if (size > 0) t.FontSize = size;
            if (margin.HasValue) t.Margin = margin.Value;
            return t;
        }

        public static Button Button(string text, Action click, bool accent = false, double minWidth = 90)
        {
            var b = new Button { Content = text, MinWidth = minWidth, Margin = new Thickness(0, 0, 8, 0) };
            if (accent) b.Style = (Style)Application.Current.FindResource("AccentBtn");
            if (click != null) b.Click += (s, e) => click();
            return b;
        }

        public static StackPanel Row(Thickness margin, params UIElement[] items)
        {
            var p = new StackPanel { Orientation = Orientation.Horizontal, Margin = margin };
            foreach (var i in items) p.Children.Add(i);
            return p;
        }

        /// <summary>A line of text that opens an address or a folder when clicked.</summary>
        public static TextBlock Link(string text, string target, Thickness? margin = null)
        {
            var h = new Hyperlink(new Run(text)) { Foreground = (Brush)Application.Current.FindResource("Accent") };
            h.Click += (s, e) => Open(target);
            var t = new TextBlock(h) { TextWrapping = TextWrapping.Wrap };
            if (margin.HasValue) t.Margin = margin.Value;
            return t;
        }

        /// <summary>Opens a web address, an ms-settings: page or a folder in Explorer.</summary>
        public static bool Open(string target)
        {
            try { Process.Start(new ProcessStartInfo(target) { UseShellExecute = true }); return true; }
            catch (Exception) { return false; }
        }
    }

    public partial class MainWindow
    {
        // ------------------------------------------------------------ preferences
        public void ShowPreferences()
        {
            var w = ToolUi.Dialog(this, "Preferences", 560);
            var p = new StackPanel { Margin = new Thickness(20) };
            p.Children.Add(ToolUi.Text("Preferences", "H1"));
            p.Children.Add(ToolUi.Text("Changes work straight away and are remembered.", "Dim", 12));

            // playing
            p.Children.Add(ToolUi.Text("Playing", "H2"));
            p.Children.Add(PrefCheck("Carry on where I left off in long videos and songs", Settings.Resume, v => { Settings.Resume = v; SaveSoon(); }));
            p.Children.Add(PrefCheck("When I open one song or video, add the others in its folder (so Next and Previous go through the folder)", Settings.FolderNeighbours, v => { Settings.FolderNeighbours = v; SaveSoon(); }));
            p.Children.Add(PrefCheck("Pause when the window is minimised", Settings.PauseOnMinimize, v => { Settings.PauseOnMinimize = v; SaveSoon(); }));
            p.Children.Add(PrefCheck("Use the graphics card to decode video (smoother, uses less power)", Settings.HardwareDecoding, v =>
            {
                if (Player.Options.HardwareDecoding != v) RebuildEngine(o => o.HardwareDecoding = v);
            }));
            p.Children.Add(ToolUi.Text("Turn this off if videos show green blocks or a black picture.", "Dim", 12, new Thickness(26, 0, 0, 4)));

            // window
            p.Children.Add(ToolUi.Text("Window", "H2"));
            var onTop = PrefChoice(new[] { ("never", "Never"), ("always", "Always"), ("playing", "While something is playing") }, Settings.OnTop, v => SetOnTop(v));
            p.Children.Add(PrefLabelled("Keep the window on top of others", onTop));
            var uiScales = new[] { 75, 90, 100, 110, 125, 150, 175, 200 };
            int curUi = (int)Math.Round(Settings.UiScale * 100);
            var uiSize = PrefChoice(PrefPercentages(uiScales, curUi), curUi.ToString(), v =>
            {
                Settings.UiScale = Math.Clamp(int.Parse(v) / 100.0, 0.75, 2.0);
                ApplyUiScale();
                SaveSoon();
            });
            p.Children.Add(PrefLabelled("Interface size (Ctrl+plus / Ctrl+minus)", uiSize));
            p.Children.Add(PrefCheck("Show the playlist when Media Player starts", Settings.ShowPlaylist, v => { if (!IsFullscreen) ShowPlaylistPanel(v); else { Settings.ShowPlaylist = v; SaveSoon(); } }));

            // subtitles
            p.Children.Add(ToolUi.Text("Subtitles", "H2"));
            var subScales = new[] { 50, 75, 100, 125, 150, 200, 250, 300 };
            var subSize = PrefChoice(PrefPercentages(subScales, Settings.SubtitleScale), Settings.SubtitleScale.ToString(), v =>
            {
                int s = int.Parse(v);
                if (Player.Options.SubtitleScale != s) RebuildEngine(o => o.SubtitleScale = s);
            });
            p.Children.Add(PrefLabelled("Subtitle size", subSize));

            // what Media Player remembers
            p.Children.Add(ToolUi.Text("What Media Player remembers", "H2"));
            var done = ToolUi.Text("", "Dim", 12, new Thickness(0, 6, 0, 0));
            p.Children.Add(ToolUi.Row(new Thickness(0),
                ToolUi.Button("Forget the list of recent files", () =>
                {
                    Settings.Recent.Clear();
                    RefreshRecent();
                    SaveSoon();
                    done.Text = "The list of recent files is empty now.";
                }, false, 0),
                ToolUi.Button("Forget where I left off in every file", () =>
                {
                    Settings.Positions.Clear();
                    RefreshRecent();
                    SaveSoon();
                    done.Text = "Every file will start from the beginning now.";
                }, false, 0)));
            p.Children.Add(done);

            // Explorer
            p.Children.Add(ToolUi.Text("File Explorer", "H2"));
            p.Children.Add(OpenWithPanel());

            var close = ToolUi.Button("Close", () => w.Close(), true);
            close.IsCancel = true; close.IsDefault = true; close.Margin = new Thickness(0);
            p.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 18, 0, 0), Children = { close } });

            w.Content = new ScrollViewer { Content = p, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MaxHeight = Math.Max(400, SystemParameters.WorkArea.Height - 80) };
            w.ShowDialog();
        }

        static (string, string)[] PrefPercentages(int[] list, int current)
        {
            var all = list.ToList();
            if (!all.Contains(current)) { all.Add(current); all.Sort(); }
            return all.Select(x => (x.ToString(), x + "%" + (x == 100 ? " (normal)" : ""))).ToArray();
        }

        static CheckBox PrefCheck(string text, bool value, Action<bool> changed)
        {
            var c = new CheckBox { Content = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap }, IsChecked = value, Margin = new Thickness(0, 4, 0, 4) };
            c.Checked += (s, e) => changed(true);
            c.Unchecked += (s, e) => changed(false);
            return c;
        }

        static ComboBox PrefChoice((string Value, string Text)[] items, string current, Action<string> changed)
        {
            var cb = new ComboBox { MinWidth = 200 };
            foreach (var (v, t) in items) cb.Items.Add(new ComboBoxItem { Content = t, Tag = v });
            cb.SelectedItem = cb.Items.Cast<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == current) ?? cb.Items[0];
            cb.SelectionChanged += (s, e) => { if (cb.SelectedItem is ComboBoxItem i) changed((string)i.Tag); };
            return cb;
        }

        static Grid PrefLabelled(string label, FrameworkElement control)
        {
            var g = new Grid { Margin = new Thickness(0, 4, 0, 4) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var t = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 12, 0) };
            g.Children.Add(t);
            Grid.SetColumn(control, 1);
            g.Children.Add(control);
            return g;
        }

        // ------------------------------------------------------------ Explorer's "Open with"
        public void ShowOpenWith()
        {
            var w = ToolUi.Dialog(this, "Open with", 520);
            var p = new StackPanel { Margin = new Thickness(20) };
            p.Children.Add(ToolUi.Text("Media Player in File Explorer", "H1"));
            p.Children.Add(OpenWithPanel());
            var close = ToolUi.Button("Close", () => w.Close(), true);
            close.IsCancel = true; close.IsDefault = true; close.Margin = new Thickness(0);
            p.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 18, 0, 0), Children = { close } });
            w.Content = p;
            w.ShowDialog();
        }

        /// <summary>The explanation, the state, Add / Remove (for this person) and the link to Windows' Default apps.</summary>
        StackPanel OpenWithPanel()
        {
            var p = new StackPanel();
            p.Children.Add(ToolUi.Text("Add Media Player to the \"Open with\" list when you right-click a video, song or playlist in File Explorer. "
                + "It also appears in Windows' Default apps, where you can choose it for some kinds of files if you want to. "
                + "This doesn't change which app opens your files now.", null, 0, new Thickness(0, 0, 0, 8)));
            var state = ToolUi.Text("", "Dim", 12, new Thickness(0, 0, 0, 8));
            p.Children.Add(state);
            Button add = null, remove = null;
            void Refresh()
            {
                var st = OpenWith.State(false);
                state.Text = st switch
                {
                    OpenWithState.Added => "Media Player is in your \"Open with\" list.",
                    OpenWithState.OtherCopy => "Another copy of Media Player (in a different folder) is in your \"Open with\" list. Add this one to use it instead.",
                    _ => "Media Player isn't in your \"Open with\" list yet.",
                };
                if (OpenWith.State(true) == OpenWithState.Added) state.Text += " It was also added for everyone on this PC.";
                add.IsEnabled = st != OpenWithState.Added;
                remove.IsEnabled = st != OpenWithState.NotAdded;
            }
            add = ToolUi.Button("Add it for me", () =>
            {
                bool ok = OpenWith.Register(false);
                Refresh();
                if (!ok) state.Text = "Media Player couldn't be added to the \"Open with\" list. Windows didn't allow the change.";
            }, true, 0);
            remove = ToolUi.Button("Remove it for me", () =>
            {
                var before = OpenWith.State(false);
                bool ok = OpenWith.Unregister(false);
                Refresh();
                if (!ok) state.Text = before == OpenWithState.OtherCopy
                    ? "The entry belongs to another copy of Media Player that's still on this PC, so it was left alone. Remove it from that copy."
                    : "Media Player couldn't be taken out of the \"Open with\" list. Windows didn't allow the change.";
            }, false, 0);
            p.Children.Add(ToolUi.Row(new Thickness(0), add, remove));
            var makeDefault = new Button { Content = "Make it my default player...", Margin = new Thickness(0, 12, 0, 0), HorizontalAlignment = HorizontalAlignment.Left };
            makeDefault.Click += (s, e) => { ShowMakeDefault(); Refresh(); };
            p.Children.Add(makeDefault);
            p.Children.Add(ToolUi.Link("Open Windows' Default apps settings", "ms-settings:defaultapps", new Thickness(0, 10, 0, 0)));
            Refresh();
            return p;
        }
    }
}
