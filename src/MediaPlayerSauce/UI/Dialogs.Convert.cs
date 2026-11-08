// Convert / save a copy (Media > Convert / save): makes a copy of a file in another format with Engine\Converter, which
// has its own libVLC, so whatever is playing carries on undisturbed. The source is never written to.
using System.IO;
using System.Windows;
using System.Windows.Controls;
using MediaPlayerSauce.Core;
using MediaPlayerSauce.Engine;

namespace MediaPlayerSauce.UI
{
    public sealed class ConvertWindow : Window
    {
        readonly MainWindow main;
        readonly Converter conv;
        readonly TextBox source, dest;
        readonly ComboBox profile;
        readonly ProgressBar bar;
        readonly TextBlock status;
        readonly Button start, cancel, browse, change, closeBtn;
        readonly StackPanel after;
        bool destChosen;     // the person picked the destination: don't change it behind their back
        bool closingAsked;

        ConvertProfile Profile => ConvertProfile.All[Math.Max(0, profile.SelectedIndex)];

        public ConvertWindow(MainWindow main)
        {
            this.main = main;
            Style = FxKit.S("Dialog");
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Owner = main;
            Title = "Convert / save a copy";
            Width = 600; SizeToContent = SizeToContent.Height; ResizeMode = ResizeMode.NoResize;
            conv = new Converter(Player.DispatcherPost(Dispatcher));
            conv.Progress += share => { bar.Value = share * 100; if (conv.Running) status.Text = "Making the copy... " + Converter.Percent(share); };
            conv.Done += file => Finished(true, "Saved as \"" + Path.GetFileName(file) + "\".", file);
            conv.Failed += why => Finished(false, why, null);
            conv.Cancelled += () => Finished(false, "Stopped. Nothing was saved.", null);

            var p = new StackPanel { Margin = new Thickness(20) };
            p.Children.Add(new TextBlock { Text = "Save a copy in another format", Style = FxKit.S("H1"), FontSize = 16 });
            p.Children.Add(FxKit.Dim("The original stays as it is. What's playing carries on while the copy is made.", 4));

            p.Children.Add(FxKit.H2("From", 16));
            source = new TextBox { Text = main.Playlist.Current?.Path ?? "" };
            browse = FxKit.Btn("Browse...", Browse);
            browse.Margin = new Thickness(8, 0, 0, 0);
            p.Children.Add(Line(source, browse));
            source.TextChanged += (s, e) => SuggestDest();

            p.Children.Add(FxKit.H2("Make it", 14));
            profile = FxKit.Combo(ConvertProfile.All.Select(x => x.Name), DefaultProfile(source.Text), i => SuggestDest());
            profile.MinWidth = 380;
            p.Children.Add(profile);
            p.Children.Add(FxKit.Dim("MP4 and MP3 play almost everywhere. FLAC keeps every detail of the sound but makes bigger files. OGV and OGG are open formats some players don't know.", 6));

            p.Children.Add(FxKit.H2("Save as", 14));
            dest = new TextBox();
            change = FxKit.Btn("Change...", ChooseDest);
            change.Margin = new Thickness(8, 0, 0, 0);
            p.Children.Add(Line(dest, change));
            dest.TextChanged += (s, e) => { if (dest.IsKeyboardFocusWithin) destChosen = true; };

            bar = new ProgressBar { Height = 6, Minimum = 0, Maximum = 100, Margin = new Thickness(0, 18, 0, 0), Foreground = FxKit.B("Accent"), Background = FxKit.B("Track"), BorderThickness = new Thickness(0) };
            p.Children.Add(bar);
            status = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0), Style = FxKit.S("Dim") };
            p.Children.Add(status);
            after = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0), Visibility = Visibility.Collapsed };
            p.Children.Add(after);

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 18, 0, 0) };
            start = new Button { Content = "Start", Style = FxKit.S("AccentBtn"), IsDefault = true, MinWidth = 90 };
            start.Click += (s, e) => Start();
            cancel = new Button { Content = "Stop", MinWidth = 90, Margin = new Thickness(8, 0, 0, 0), IsEnabled = false };
            cancel.Click += (s, e) => conv.Cancel();
            closeBtn = new Button { Content = "Close", MinWidth = 90, Margin = new Thickness(8, 0, 0, 0), IsCancel = true };
            closeBtn.Click += (s, e) => Close();
            buttons.Children.Add(start); buttons.Children.Add(cancel); buttons.Children.Add(closeBtn);
            p.Children.Add(buttons);
            Content = p;
            SuggestDest();

            Closing += (s, e) =>
            {
                if (!conv.Running || closingAsked) return;
                var r = MessageBox.Show(this, "A copy is still being made. Stop it and close?", "Convert / save a copy", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (r != MessageBoxResult.Yes) { e.Cancel = true; return; }
                closingAsked = true;
                conv.Cancel();
            };
            Closed += (s, e) => conv.Dispose();
        }

        static UIElement Line(TextBox box, Button btn)
        {
            var d = new DockPanel();
            DockPanel.SetDock(btn, Dock.Right);
            d.Children.Add(btn); d.Children.Add(box);
            return d;
        }

        static int DefaultProfile(string src)
        {
            if (!string.IsNullOrEmpty(src) && MediaFiles.IsAudio(src)) return Array.FindIndex(ConvertProfile.All, x => x.Ext == ".mp3");
            return 0;
        }

        public void SetSource(string path)
        {
            if (conv.Running || string.IsNullOrEmpty(path)) return;
            source.Text = path;
        }

        void SuggestDest()
        {
            if (dest == null || conv.Running) return;
            string src = source.Text.Trim();
            if (destChosen && dest.Text.Length > 0)
            {
                // keep their folder and name, but follow the chosen format's extension
                try { dest.Text = Path.ChangeExtension(dest.Text, Profile.Ext); } catch (Exception) { }
                return;
            }
            dest.Text = src.Length == 0 ? "" : Converter.DefaultDestination(src, Profile);
        }

        void Browse()
        {
            string cur = source.Text.Trim();
            var d = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Choose a file to convert",
                Filter = "Media files|" + string.Join(";", MediaFiles.Video.Concat(MediaFiles.Audio).Select(x => "*" + x)) + "|All files|*.*",
            };
            try
            {
                string dir = !MediaFiles.IsUrl(cur) && cur.Length > 0 ? Path.GetDirectoryName(Path.GetFullPath(cur)) : main.Settings.LastFolder;
                if (Directory.Exists(dir)) d.InitialDirectory = dir;
            }
            catch (Exception) { }
            if (d.ShowDialog(this) != true) return;
            destChosen = false;
            profile.SelectedIndex = Math.Max(0, DefaultProfile(d.FileName));
            source.Text = d.FileName;
            SuggestDest();
        }

        void ChooseDest()
        {
            var prof = Profile;
            string cur = dest.Text.Trim();
            var d = new Microsoft.Win32.SaveFileDialog
            {
                Title = "Save the copy as",
                Filter = prof.Name + "|*" + prof.Ext + "|All files|*.*",
                DefaultExt = prof.Ext,
                AddExtension = true,
                OverwritePrompt = true,
            };
            try
            {
                if (cur.Length > 0)
                {
                    d.FileName = Path.GetFileName(cur);
                    string dir = Path.GetDirectoryName(Path.GetFullPath(cur));
                    if (Directory.Exists(dir)) d.InitialDirectory = dir;
                }
            }
            catch (Exception) { }
            if (d.ShowDialog(this) != true) return;
            if (Converter.SamePath(d.FileName, source.Text.Trim()))
            {
                Say("The copy can't replace the file it's made from. Choose another name.", true);
                return;
            }
            destChosen = true;
            dest.Text = d.FileName;
            // the Save dialog already asked about replacing an existing file
            replaceAgreed = d.FileName;
        }
        string replaceAgreed;

        void Say(string text, bool warn)
        {
            status.Text = text;
            status.Foreground = warn ? FxKit.B("Warn") : FxKit.B("TextDim");
        }

        void Start()
        {
            string src = source.Text.Trim().Trim('"'), dst = dest.Text.Trim().Trim('"');
            after.Visibility = Visibility.Collapsed;
            if (src.Length == 0) { Say("Choose a file to convert first.", true); return; }
            if (dst.Length == 0) { Say("Choose where to save the copy.", true); return; }
            try
            {
                if (!Path.IsPathRooted(dst) && !MediaFiles.IsUrl(src)) dst = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(src)), dst);
                dst = Path.GetFullPath(dst);
                if (Path.GetExtension(dst).Length == 0) dst += Profile.Ext;
            }
            catch (Exception) { Say("That isn't a name a file can have. Choose another.", true); return; }
            if (!MediaFiles.IsUrl(src) && Converter.SamePath(src, dst)) { Say("The copy can't replace the file it's made from. Choose another name.", true); return; }
            if (File.Exists(dst) && !string.Equals(replaceAgreed, dst, StringComparison.OrdinalIgnoreCase))
            {
                var r = MessageBox.Show(this, "\"" + Path.GetFileName(dst) + "\" is already there.\n\nYes replaces it, No saves the copy as \"" +
                    Path.GetFileName(Converter.FreeName(dst)) + "\" instead.", "Convert / save a copy", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
                if (r == MessageBoxResult.Cancel || r == MessageBoxResult.None) return;
                if (r == MessageBoxResult.No) dst = Converter.FreeName(dst);
            }
            dest.Text = dst;
            try { conv.Start(src, dst, Profile); }
            catch (ArgumentException ex) { Say(ex.Message, true); return; }
            catch (Exception ex) { Say("The copy couldn't be started: " + ex.Message, true); return; }
            bar.Value = 0;
            Say("Making the copy...", false);
            SetBusy(true);
        }

        void SetBusy(bool busy)
        {
            start.IsEnabled = !busy; cancel.IsEnabled = busy;
            source.IsEnabled = dest.IsEnabled = profile.IsEnabled = browse.IsEnabled = change.IsEnabled = !busy;
        }

        void Finished(bool ok, string text, string file)
        {
            SetBusy(false);
            replaceAgreed = null;
            if (!ok) bar.Value = 0;
            Say(text, !ok);
            after.Children.Clear();
            if (ok && file != null)
            {
                after.Children.Add(FxKit.Btn("Play it", () => main.OpenPaths(new[] { file })));
                after.Children.Add(FxKit.Btn("Show in folder", () =>
                {
                    try { System.Diagnostics.Process.Start("explorer.exe", "/select,\"" + file + "\""); } catch (Exception) { }
                }));
                after.Visibility = Visibility.Visible;
                main.ShowOsd("Copy saved");
                // the next copy shouldn't land on this one
                destChosen = false;
            }
        }

    }

    public partial class MainWindow
    {
        ConvertWindow convertWindow;

        public void ShowConvert()
        {
            if (convertWindow == null)
            {
                convertWindow = new ConvertWindow(this);
                convertWindow.Closed += (s, e) => convertWindow = null;
                convertWindow.Show();
                return;
            }
            convertWindow.SetSource(Playlist.Current?.Path);
            if (convertWindow.WindowState == WindowState.Minimized) convertWindow.WindowState = WindowState.Normal;
            convertWindow.Activate();
        }
    }
}
