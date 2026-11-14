// Every slider (the seek bar, the volume, the equalizer) jumps to where it is pressed AND keeps following the pointer
// while the button is held. WPF's own IsMoveToPointEnabled jumps but then doesn't drag: the press goes to the track,
// not the knob, so only grabbing the knob exactly could drag. Registered once for every Slider in the program.
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace MediaPlayerSauce.UI
{
    public static class SliderDrag
    {
        static bool registered;
        static Slider dragging;

        public static void Register()
        {
            if (registered) return;
            registered = true;
            EventManager.RegisterClassHandler(typeof(Slider), UIElement.PreviewMouseLeftButtonDownEvent, new MouseButtonEventHandler(Down), true);
            EventManager.RegisterClassHandler(typeof(Slider), UIElement.PreviewMouseMoveEvent, new MouseEventHandler(Move), true);
            EventManager.RegisterClassHandler(typeof(Slider), UIElement.PreviewMouseLeftButtonUpEvent, new MouseButtonEventHandler(Up), true);
            EventManager.RegisterClassHandler(typeof(Slider), UIElement.LostMouseCaptureEvent, new MouseEventHandler((s, e) => { if (ReferenceEquals(s, dragging)) dragging = null; }), true);
        }

        /// <summary>The slider's value under the pointer, or null when it has no track yet.</summary>
        public static double? ValueAt(Slider s, MouseEventArgs e)
        {
            if (s.Template?.FindName("PART_Track", s) is not Track track || track.ActualWidth <= 0 && track.ActualHeight <= 0) return null;
            return Math.Clamp(track.ValueFromPoint(e.GetPosition(track)), s.Minimum, s.Maximum);
        }

        static void Down(object sender, MouseButtonEventArgs e)
        {
            if (sender is not Slider s || !s.IsEnabled || e.ClickCount > 1) return;
            var v = ValueAt(s, e);
            if (v == null) return;
            s.Focus();
            s.Value = v.Value;
            dragging = s;
            s.CaptureMouse();
            e.Handled = true;
        }

        static void Move(object sender, MouseEventArgs e)
        {
            if (!ReferenceEquals(sender, dragging) || e.LeftButton != MouseButtonState.Pressed) return;
            var v = ValueAt(dragging, e);
            if (v != null) dragging.Value = v.Value;
        }

        static void Up(object sender, MouseButtonEventArgs e)
        {
            if (!ReferenceEquals(sender, dragging)) return;
            var s = dragging;
            dragging = null;
            s.ReleaseMouseCapture();
        }
    }
}
