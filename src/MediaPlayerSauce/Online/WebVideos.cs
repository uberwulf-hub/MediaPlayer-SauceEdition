// Videos that only play in a browser (YouTube) open in their own Edge window (not written yet).
using System.Windows;

namespace MediaPlayerSauce.Online
{
    public static class WebVideos
    {
        public static bool IsWebOnly(string url) { return false; }
        public static void OpenInEdge(string url, Window owner) { }
        public static void CloseAll() { }
    }
}
