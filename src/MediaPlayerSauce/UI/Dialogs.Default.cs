// "Make it my default player": adds Media Player to Windows' Default apps (the same registration as "Open with"),
// opens its own page there, and shows, as the person makes the choice, how many kinds of files now open with it.
// Windows only lets the person make this choice, so nothing here changes it behind their back.
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using MediaPlayerSauce.Core;

namespace MediaPlayerSauce.UI
{
    public partial class MainWindow
    {
        public void ShowMakeDefault()
        {
            Settings.DefaultAsked = true;
            SaveSoon();
            var w = ToolUi.Dialog(this, "Make it your default player", 560);
            var p = new StackPanel { Margin = new Thickness(20) };
            p.Children.Add(ToolUi.Text("Open your videos and music with Media Player", "H1"));
            p.Children.Add(ToolUi.Text("Windows asks you to make this choice yourself, in its Settings. Media Player opens the right page for you:", null, 0, new Thickness(0, 0, 0, 10)));
            string steps = DefaultApp.IsWindows11
                ? "1.  Click \"Open the settings page\" below.\n2.  At the top of the page, click \"Set default\".\n3.  Come back here: the line below turns green."
                : "1.  Click \"Open the settings page\" below.\n2.  Under \"Video player\", click the app shown and choose Media Player (Sauce Edition).\n3.  Do the same under \"Music player\".\n4.  Come back here: the line below turns green.";
            p.Children.Add(new Border
            {
                Background = (System.Windows.Media.Brush)FindResource("Panel2"), CornerRadius = new CornerRadius(6), Padding = new Thickness(14, 10, 14, 10),
                Child = ToolUi.Text(steps, null, 13)
            });
            var status = ToolUi.Text("", null, 13, new Thickness(0, 14, 0, 0));
            p.Children.Add(status);
            var open = ToolUi.Button("Open the settings page", () =>
            {
                if (OpenWith.State(false) != OpenWithState.Added) OpenWith.Register(false);
                if (!ToolUi.Open(DefaultApp.SettingsAddress())) ToolUi.Open("ms-settings:defaultapps");
            }, true, 0);
            var close = ToolUi.Button("Close", () => w.Close(), false, 0);
            close.IsCancel = true;
            p.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 18, 0, 0), Children = { open, close } });
            w.Content = p;

            void Refresh()
            {
                var s = DefaultApp.Status();
                status.Text = (s.All ? "✔  " : "") + DefaultApp.Describe(s);
                status.Foreground = (System.Windows.Media.Brush)FindResource(s.All ? "Accent" : "TextDim");
                open.Content = s.All ? "Open the settings page again" : "Open the settings page";
            }
            Refresh();
            // the choice is made in Settings, so look again every couple of seconds while this is open
            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            timer.Tick += (s, e) => Refresh();
            w.Activated += (s, e) => Refresh();
            timer.Start();
            w.Closed += (s, e) => { timer.Stop(); UpdateDefaultPrompt(); };
            w.ShowDialog();
        }

        /// <summary>The start page's one-time question: shown until it's answered, and never once Media Player is the default.</summary>
        void UpdateDefaultPrompt()
        {
            bool show = !Quiet && !Settings.DefaultAsked && !DefaultApp.Status().All;
            DefaultPrompt.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        }
    }
}
