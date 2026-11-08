// Every window of the app gets Windows' dark title bar (Windows 10 2004 and later, Windows 11), so it matches the
// dark window. Older Windows simply keeps its own.
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace MediaPlayerSauce.UI
{
    public static class DarkTitle
    {
        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
        const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20, DWMWA_CAPTION_COLOR = 35;

        /// <summary>Applies to every window the app opens from now on.</summary>
        public static void ForAllWindows()
        {
            EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.LoadedEvent, new RoutedEventHandler((s, e) => Apply(s as Window)));
        }

        public static void Apply(Window w)
        {
            if (w == null) return;
            try
            {
                IntPtr h = new WindowInteropHelper(w).Handle;
                if (h == IntPtr.Zero) return;
                int on = 1;
                DwmSetWindowAttribute(h, DWMWA_USE_IMMERSIVE_DARK_MODE, ref on, 4);
                int color = 0x00171414;   // COLORREF 0x00BBGGRR: the window's own background, #141417
                DwmSetWindowAttribute(h, DWMWA_CAPTION_COLOR, ref color, 4);
            }
            catch (Exception) { }
        }
    }
}
