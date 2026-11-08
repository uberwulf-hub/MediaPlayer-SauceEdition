// Media > Play a disc (Ctrl+D): the PC's disc drives with what's in them, or a disc folder / disc image copied to the
// PC, the kind of disc (chosen by itself when it can tell), an optional start, then Play. The work is in Core\Discs.cs.
using System.IO;
using System.Windows;
using System.Windows.Controls;
using MediaPlayerSauce.Core;

namespace MediaPlayerSauce.UI
{
    sealed class DiscSource
    {
        public string Location;      // "D:\", a folder or an image file
        public bool Drive, Image, Ready = true, HasFiles;
        public DiscKind Kind;
        public string Text;
        public override string ToString() { return Text; }
    }

    public partial class MainWindow
    {
        bool discErrorsHooked;

        public void ShowOpenDisc()
        {
            HookDiscErrors();
            var w = ToolUi.Dialog(this, "Play a disc", 580);
            var p = new StackPanel { Margin = new Thickness(20) };
            p.Children.Add(ToolUi.Text("Play a disc", "H1"));

            p.Children.Add(ToolUi.Text("Disc drives", "H2"));
            var list = new ListBox { Style = (Style)FindResource("PlainList"), ItemContainerStyle = (Style)FindResource("PlainItem"), MaxHeight = 170, Margin = new Thickness(-4, 0, -4, 0) };
            var empty = ToolUi.Text("", "Dim", 12, new Thickness(0, 2, 0, 4));
            p.Children.Add(list);
            p.Children.Add(empty);
            var extra = new List<DiscSource>();

            void Fill(DiscSource select)
            {
                var sources = Discs.Drives().Select(d => new DiscSource
                {
                    Location = d.Root, Drive = true, Ready = d.Ready, Kind = d.Kind, HasFiles = d.HasFiles, Text = d.Describe()
                }).ToList();
                int drives = sources.Count;
                sources.AddRange(extra);
                list.ItemsSource = sources;
                empty.Text = drives == 0 ? "This PC has no disc drive. You can still play a disc folder (VIDEO_TS or BDMV) copied to the PC." :
                             sources.Take(drives).All(s => !s.Ready) ? "There's no disc in " + (drives == 1 ? "the drive" : "any of the drives") + ". Put one in, wait a few seconds, then press Look again." : "";
                empty.Visibility = empty.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
                list.SelectedItem = select != null ? sources.FirstOrDefault(s => s.Location == select.Location) : sources.FirstOrDefault(s => s.Ready && s.Kind != DiscKind.Unknown) ?? sources.FirstOrDefault(s => s.Ready);
            }

            var btnAgain = ToolUi.Button("Look again", () => Fill(list.SelectedItem as DiscSource), false, 0);
            var btnFolder = ToolUi.Button("Open a disc folder...", () =>
            {
                var d = new Microsoft.Win32.OpenFolderDialog { Title = "Choose a disc folder (one with VIDEO_TS, BDMV or MPEGAV in it)" };
                if (d.ShowDialog(w) != true) return;
                var kind = Discs.Detect(d.FolderName, false, out bool files);
                var s = new DiscSource { Location = d.FolderName, Kind = kind, HasFiles = files, Text = "Folder   " + d.FolderName + (kind != DiscKind.Unknown ? "  -  " + Discs.KindName(kind) : "") };
                extra.Add(s);
                Fill(s);
            }, false, 0);
            var btnImage = ToolUi.Button("Open a disc image (.iso)...", () =>
            {
                var d = new Microsoft.Win32.OpenFileDialog { Title = "Choose a disc image", Filter = "Disc images|*.iso;*.cue;*.bin;*.img|All files|*.*" };
                if (d.ShowDialog(w) != true) return;
                var kind = Discs.DetectIso(d.FileName);
                var s = new DiscSource { Location = d.FileName, Image = true, Kind = kind, Text = "Image   " + Path.GetFileName(d.FileName) + (kind != DiscKind.Unknown ? "  -  " + Discs.KindName(kind) : "") };
                extra.Add(s);
                Fill(s);
            }, false, 0);
            p.Children.Add(ToolUi.Row(new Thickness(0, 6, 0, 0), btnAgain, btnFolder, btnImage));

            p.Children.Add(ToolUi.Text("Kind of disc", "H2"));
            var kinds = new (DiscKind Kind, string Text)[] { (DiscKind.Dvd, "DVD"), (DiscKind.BluRay, "Blu-ray"), (DiscKind.AudioCd, "Audio CD"), (DiscKind.VideoCd, "Video CD"), (DiscKind.Unknown, "Files on a data disc") };
            var radios = new Dictionary<DiscKind, RadioButton>();
            var kindRow = new WrapPanel();
            foreach (var (k, t) in kinds)
            {
                var r = new RadioButton { Content = t, GroupName = "disckind", Margin = new Thickness(0, 2, 18, 2) };
                radios[k] = r;
                kindRow.Children.Add(r);
            }
            p.Children.Add(kindRow);

            p.Children.Add(ToolUi.Text("Start at (optional)", "H2"));
            var lblTitle = new TextBlock { Text = "Title", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
            var txtTitle = new TextBox { Width = 60 };
            var lblChapter = new TextBlock { Text = "Chapter", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(18, 0, 8, 0) };
            var txtChapter = new TextBox { Width = 60 };
            p.Children.Add(ToolUi.Row(new Thickness(0), lblTitle, txtTitle, lblChapter, txtChapter));
            var startHint = ToolUi.Text("", "Dim", 12, new Thickness(0, 6, 0, 0));
            p.Children.Add(startHint);

            p.Children.Add(ToolUi.Text("Disc menus aren't shown: the film starts straight away. " + Discs.ProtectedNote, "Dim", 12, new Thickness(0, 14, 0, 0)));
            var error = new TextBlock { Foreground = (System.Windows.Media.Brush)FindResource("Warn"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0), Visibility = Visibility.Collapsed };
            p.Children.Add(error);

            DiscKind Chosen() { return radios.FirstOrDefault(r => r.Value.IsChecked == true).Key; }
            bool AnyChosen() { return radios.Values.Any(r => r.IsChecked == true); }
            void UpdateStart()
            {
                var k = Chosen();
                bool any = AnyChosen();
                bool title = any && (k == DiscKind.Dvd || k == DiscKind.BluRay || k == DiscKind.VideoCd);
                bool chapter = any && (k == DiscKind.BluRay || k == DiscKind.VideoCd);
                txtTitle.IsEnabled = lblTitle.IsEnabled = title;
                txtChapter.IsEnabled = lblChapter.IsEnabled = chapter;
                lblTitle.Opacity = title ? 1 : 0.45; lblChapter.Opacity = chapter ? 1 : 0.45;
                startHint.Text = !any ? "" : k switch
                {
                    DiscKind.Dvd => "Leave it empty for the main film. A title number plays that part of the DVD instead (its VTS_nn files).",
                    DiscKind.BluRay or DiscKind.VideoCd => "Leave both empty to start at the beginning.",
                    DiscKind.AudioCd => "Every track on the CD goes into the playlist; double-click one to start there.",
                    _ => "The disc's videos and music go into the playlist.",
                };
            }
            foreach (var r in radios.Values) { r.Checked += (s, e) => { UpdateStart(); error.Visibility = Visibility.Collapsed; }; }
            list.SelectionChanged += (s, e) =>
            {
                error.Visibility = Visibility.Collapsed;
                if (list.SelectedItem is DiscSource src && src.Ready && (src.Kind != DiscKind.Unknown || src.HasFiles)) radios[src.Kind].IsChecked = true;
            };

            var play = ToolUi.Button("Play", null, true);
            play.IsDefault = true;
            var cancel = ToolUi.Button("Cancel", null);
            cancel.IsCancel = true; cancel.Margin = new Thickness(0);
            play.Click += (s, e) =>
            {
                string why = PlayDisc(list.SelectedItem as DiscSource, AnyChosen() ? Chosen() : (DiscKind?)null, txtTitle.Text, txtChapter.Text);
                if (why != null) { error.Text = why; error.Visibility = Visibility.Visible; return; }
                w.DialogResult = true;
            };
            p.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 18, 0, 0), Children = { play, cancel } });

            w.Content = p;
            Fill(null);
            UpdateStart();
            w.ShowDialog();
        }

        /// <summary>Plays the disc; an error sentence for the person when it can't, else null.</summary>
        string PlayDisc(DiscSource src, DiscKind? chosen, string titleText, string chapterText)
        {
            if (src == null) return "Choose a disc drive, or open a disc folder or disc image.";
            if (src.Drive && !src.Ready) return "There's no disc in drive " + src.Location.TrimEnd('\\') + ". Put one in, wait a few seconds, then press Look again.";
            if (chosen == null) return "Choose the kind of disc.";
            var kind = chosen.Value;
            if (!ReadNumber(titleText, out int title)) return "The title must be a number, like 1 or 2 (or leave it empty).";
            if (!ReadNumber(chapterText, out int chapter)) return "The chapter must be a number, like 1 or 5 (or leave it empty).";
            string where = src.Drive ? "drive " + src.Location.TrimEnd('\\') : "\"" + Path.GetFileName(src.Location.TrimEnd('\\')) + "\"";

            switch (kind)
            {
                case DiscKind.Dvd:
                {
                    if (src.Image)
                        return "A DVD's disc image can't be opened directly. Double-click it in File Explorer: Windows shows it as a new drive. Then press Look again and choose that drive.";
                    string vts = Discs.VideoTsFolder(src.Location);
                    if (vts == null) return "There's no DVD video (a VIDEO_TS folder) on " + where + ".";
                    var set = Discs.MainTitleSet(vts, title);
                    if (set == null)
                        return title > 0 ? "This DVD has no title " + title + ". Leave the title empty to play the main film."
                                         : "The DVD's video files couldn't be found or read. " + Discs.ProtectedNote;
                    OpenPaths(set.Files);
                    return null;
                }
                case DiscKind.Unknown:
                {
                    if (src.Image) return "Double-click the disc image in File Explorer: Windows shows it as a new drive. Then press Look again and choose that drive.";
                    var files = MediaFiles.ExpandFolder(src.Location);
                    if (files.Count == 0) return "There are no videos or music on " + where + " that Media Player can play.";
                    OpenPaths(files);
                    return null;
                }
                case DiscKind.VideoCd when !src.Drive && !src.Image:
                {
                    // a video CD copied to the PC: its MPEGAV\*.DAT (or SVCD's MPEG2\*.MPG) files play as they are
                    var files = new List<string>();
                    foreach (var (dir, pattern) in new[] { ("MPEGAV", "*.dat"), ("MPEG2", "*.mpg") })
                    {
                        string d = Path.GetFileName(src.Location.TrimEnd('\\')).Equals(dir, StringComparison.OrdinalIgnoreCase) ? src.Location : Path.Combine(src.Location, dir);
                        try { if (Directory.Exists(d)) files.AddRange(Directory.GetFiles(d, pattern).OrderBy(f => f, StringComparer.OrdinalIgnoreCase)); } catch (Exception) { }
                    }
                    if (files.Count == 0) return "There's no video CD (an MPEGAV folder) in " + where + ".";
                    OpenPaths(files);
                    return null;
                }
                case DiscKind.AudioCd when !src.Drive:
                    return "Audio CDs play from a disc drive. Choose the drive the CD is in.";
            }
            string addr = Discs.Address(kind, src.Location, title, chapter);
            if (addr == null) return "This can't be played as a " + Discs.KindName(kind) + ". Try another kind of disc.";
            OpenPaths(new[] { addr });
            return null;
        }

        static bool ReadNumber(string text, out int n)
        {
            n = 0;
            text = (text ?? "").Trim();
            if (text.Length == 0) return true;
            return int.TryParse(text, out n) && n >= 1 && n <= 999;
        }

        /// <summary>After a disc fails, a sentence about copy protection follows libVLC's own message.</summary>
        void HookDiscErrors()
        {
            if (discErrorsHooked) return;
            discErrorsHooked = true;
            Player.Failed += m =>
            {
                string cur = Player.CurrentPath ?? "";
                if (cur.StartsWith("bluray:", StringComparison.OrdinalIgnoreCase)) ShowNotice(Discs.BluRayFailed);
                else if (cur.IndexOf(@"\VIDEO_TS\", StringComparison.OrdinalIgnoreCase) >= 0 && cur.EndsWith(".vob", StringComparison.OrdinalIgnoreCase))
                    ShowNotice("This DVD couldn't be played. " + Discs.ProtectedNote);
            };
        }
    }
}
