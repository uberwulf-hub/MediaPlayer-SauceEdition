// YouTube, Netflix and Prime Video are not media streams Media Player can open (YouTube splits videos into streams only
// a browser plays; Netflix and Prime Video are copy-protected): they open in an Edge app window (no tabs or address
// bar; Edge also plays Netflix and Prime Video in the best quality), or the default browser when Edge is missing.
// Media Player pauses meanwhile. Edge windows are never closed by Media Player (they may be the person's own).
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using Microsoft.Win32;

namespace MediaPlayerSauce.Online
{
    /// <summary>A copy-protected service that plays in its own Edge window.</summary>
    public sealed class StreamService
    {
        public string Key { get; init; }
        public string Name { get; init; }
        public string Color { get; init; }
        public string Home { get; init; }
        /// <summary>Search address with {0} for the escaped words.</summary>
        public string Search { get; init; }
        public string Text { get; init; }
        public string SearchUrl(string words) => string.Format(Search, Uri.EscapeDataString((words ?? "").Trim()));
    }

    public static class WebVideos
    {
        public static readonly StreamService Netflix = new StreamService
        {
            Key = "Netflix", Name = "Netflix", Color = "#E50914",
            Home = "https://www.netflix.com/browse", Search = "https://www.netflix.com/search?q={0}",
            Text = "Netflix movies and shows are copy-protected, so they play in their own Edge window instead of in Media Player. Sign in there once and Edge remembers you. Type in the search box above to search Netflix.",
        };
        public static readonly StreamService Prime = new StreamService
        {
            Key = "Prime", Name = "Prime Video", Color = "#1A98FF",
            Home = "https://www.amazon.com/gp/video/storefront", Search = "https://www.amazon.com/s?k={0}&i=instant-video",
            Text = "Prime Video movies and shows are copy-protected, so they play in their own Edge window instead of in Media Player. Sign in with your Amazon account there once and Edge remembers you. Type in the search box above to search Prime Video.",
        };

        static readonly Regex YouTubeHost = new Regex(@"^(?:(?:www|m|music)\.)?youtube(?:-nocookie)?\.com$|^youtu\.be$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        static readonly Regex NetflixHost = new Regex(@"^(?:[a-z0-9-]+\.)*netflix\.com$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        static readonly Regex PrimeHost = new Regex(@"^(?:[a-z0-9-]+\.)*primevideo\.com$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        static readonly Regex AmazonHost = new Regex(@"^(?:www\.|smile\.)?amazon\.(?:com|ca|com\.mx|com\.br|co\.uk|de|fr|it|es|nl|se|pl|in|co\.jp|com\.au)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        /// <summary>The address as an absolute http(s) Uri, or null.</summary>
        static Uri Parse(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return null;
            if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var u)) return null;
            return u.Scheme == Uri.UriSchemeHttp || u.Scheme == Uri.UriSchemeHttps ? u : null;
        }

        /// <summary>Which service an address belongs to: "YouTube", "Netflix", "Prime" or null.</summary>
        public static string ServiceOf(string url)
        {
            var u = Parse(url);
            if (u == null) return null;
            string host = u.Host;
            if (YouTubeHost.IsMatch(host)) return "YouTube";
            if (NetflixHost.IsMatch(host)) return "Netflix";
            if (PrimeHost.IsMatch(host)) return "Prime";
            if (AmazonHost.IsMatch(host))
            {
                string path = u.AbsolutePath;
                if (path.StartsWith("/gp/video", StringComparison.OrdinalIgnoreCase) || path.Contains("/Prime-Video", StringComparison.OrdinalIgnoreCase)
                    || path.StartsWith("/Amazon-Video", StringComparison.OrdinalIgnoreCase)
                    || Regex.IsMatch(u.Query, @"(?:^\?|&)i=instant-video(?:&|$)", RegexOptions.IgnoreCase)) return "Prime";
            }
            return null;
        }

        /// <summary>True for addresses that only play in a browser: YouTube, Netflix and Prime Video.</summary>
        public static bool IsWebOnly(string url) => ServiceOf(url) != null;

        /// <summary>The address rebuilt from its parsed parts: always https, characters escaped (safe on a command line).</summary>
        public static string WebUrl(string url)
        {
            var u = Parse(url);
            if (u == null) return null;
            return "https://" + u.Host + u.PathAndQuery + u.Fragment;
        }

        static string NameOf(string url)
        {
            switch (ServiceOf(url))
            {
                case "Netflix": return "Netflix";
                case "Prime": return "Prime Video";
                case "YouTube": return "YouTube";
                default: return "The page";
            }
        }

        /// <summary>msedge.exe from the App Paths registry keys (as Windows finds it), or null.</summary>
        public static string FindEdge()
        {
            foreach (var root in new[] { Registry.LocalMachine, Registry.CurrentUser })
            {
                try
                {
                    using var k = root.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\msedge.exe");
                    string p = (k?.GetValue(null) as string)?.Trim().Trim('"');
                    if (!string.IsNullOrEmpty(p) && File.Exists(p)) return p;
                }
                catch (Exception) { }
            }
            return null;
        }

        /// <summary>Edge's command line for an app window of the address.</summary>
        public static string EdgeArguments(string url) => "--app=\"" + url.Replace("\"", "%22") + "\"";

        /// <summary>Opens the address in its own Edge app window (the default browser without Edge) and pauses the player.</summary>
        public static void OpenInEdge(string url, Window owner)
        {
            string safe = WebUrl(url);
            var main = (owner as UI.MainWindow) ?? (owner?.Owner as UI.MainWindow) ?? (Application.Current?.MainWindow as UI.MainWindow);
            if (safe == null) { main?.ShowNotice("That address can't be opened in a browser."); return; }
            string name = NameOf(safe);
            try { if (main?.Player != null && main.Player.IsPlaying) main.Player.Pause(); } catch (Exception) { }
            if (main != null && main.Quiet)   // snapshot runs open no windows
            {
                Console.Error.WriteLine("web app: " + safe);
                main.ShowOsd(name + " opened in its own window", 3);
                return;
            }
            try
            {
                string edge = FindEdge();
                if (edge != null) Process.Start(new ProcessStartInfo(edge, EdgeArguments(safe)) { UseShellExecute = false })?.Dispose();
                else Process.Start(new ProcessStartInfo(safe) { UseShellExecute = true })?.Dispose();
                main?.ShowOsd(name + " opened in its own window", 3);
            }
            catch (Exception ex)
            {
                string text = name + " couldn't be opened (" + ex.Message + "). The address was " + safe;
                if (main != null) main.ShowNotice(text);
                else MessageBox.Show(text, "Media Player", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        /// <summary>Nothing to close: the Edge windows belong to Edge (and the person may have opened others).</summary>
        public static void CloseAll() { }
    }
}
