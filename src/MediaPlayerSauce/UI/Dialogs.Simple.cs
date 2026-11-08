// Small dialogs built in code: open a network stream, jump to a time. InputBox is shared by both (and by others).
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MediaPlayerSauce.Core;

namespace MediaPlayerSauce.UI
{
    public sealed class InputBox : Window
    {
        readonly TextBox box;
        readonly TextBlock error;
        readonly Func<string, string> check;
        public string Value => box.Text.Trim();

        /// <summary>check returns an error sentence for a value that won't do, or null.</summary>
        public InputBox(Window owner, string title, string prompt, string value, string hint, string okText, Func<string, string> check)
        {
            Style = (Style)Application.Current.FindResource("Dialog");
            Owner = owner;
            Title = title;
            Width = 520; SizeToContent = SizeToContent.Height; ResizeMode = ResizeMode.NoResize;
            this.check = check;
            var p = new StackPanel { Margin = new Thickness(20) };
            p.Children.Add(new TextBlock { Text = prompt, Style = (Style)FindResource("H1"), FontSize = 15 });
            box = new TextBox { Text = value ?? "", Margin = new Thickness(0, 4, 0, 0) };
            p.Children.Add(box);
            if (!string.IsNullOrEmpty(hint)) p.Children.Add(new TextBlock { Text = hint, Style = (Style)FindResource("Dim"), FontSize = 12, Margin = new Thickness(0, 8, 0, 0) });
            error = new TextBlock { Foreground = (System.Windows.Media.Brush)FindResource("Warn"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0), Visibility = Visibility.Collapsed };
            p.Children.Add(error);
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 18, 0, 0) };
            var ok = new Button { Content = okText, Style = (Style)FindResource("AccentBtn"), IsDefault = true, MinWidth = 90 };
            var cancel = new Button { Content = "Cancel", IsCancel = true, MinWidth = 90, Margin = new Thickness(8, 0, 0, 0) };
            ok.Click += (s, e) =>
            {
                string why = this.check?.Invoke(Value);
                if (why != null) { error.Text = why; error.Visibility = Visibility.Visible; box.Focus(); box.SelectAll(); return; }
                DialogResult = true;
            };
            buttons.Children.Add(ok); buttons.Children.Add(cancel);
            p.Children.Add(buttons);
            Content = p;
            Loaded += (s, e) => { box.Focus(); box.SelectAll(); };
        }
    }

    public partial class MainWindow
    {
        public void ShowOpenUrl()
        {
            string guess = "";
            try { if (Clipboard.ContainsText()) { string c = Clipboard.GetText().Trim(); if (MediaFiles.IsUrl(c) && c.Length < 2000) guess = c; } } catch (Exception) { }
            var d = new InputBox(this, "Open a network stream", "Address of the stream or video", guess,
                "For example https://.../video.mp4, a radio station's address, rtsp://camera/stream, or a YouTube link (YouTube opens in its own Edge window).",
                "Play", v => MediaFiles.IsUrl(v) ? null : (v.Length == 0 ? "Type or paste an address." : "That doesn't look like an address. It should start with something like https:// or rtsp://."));
            if (d.ShowDialog() == true) OpenPaths(new[] { d.Value });
        }

        public void ShowJump()
        {
            if (!Player.HasMedia || !Player.Seekable) { ShowOsd("This can't be jumped in"); return; }
            long len = Player.Length;
            var d = new InputBox(this, "Jump to a time", "Go to", MediaFiles.Time(Player.Time),
                "Like 1:30, 1:02:03, 90 (seconds) or 1h2m. " + (len > 0 ? "This one is " + MediaFiles.Time(len) + " long." : ""),
                "Go", v =>
                {
                    long t = MediaFiles.ParseTime(v);
                    if (t < 0) return "That isn't a time. Try something like 1:30.";
                    if (len > 0 && t > len) return "That's after the end (" + MediaFiles.Time(len) + ").";
                    return null;
                });
            if (d.ShowDialog() == true) { Player.Time = MediaFiles.ParseTime(d.Value); ShowOsd("Jumped to " + MediaFiles.Time(MediaFiles.ParseTime(d.Value))); }
        }
    }
}
