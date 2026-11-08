// "Make it my default player": reading which app opens each kind of file (in a test registry key, never the real
// one), the sentences, and the Settings address.
using Microsoft.Win32;
using MediaPlayerSauce.Core;

namespace MediaPlayerSE.Tests
{
    static partial class Program
    {
        static void DefaultChecks()
        {
            string path = @"Software\MediaPlayerSE-Test-" + Guid.NewGuid().ToString("N");
            try
            {
                using var root = Registry.CurrentUser.CreateSubKey(path);
                void Choose(string ext, string progId)
                {
                    using var k = root.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\FileExts\" + ext + @"\UserChoice");
                    k.SetValue("ProgId", progId);
                }
                var none = DefaultApp.Status(root);
                Check("default: nothing chosen counts as another app", none.None && none.Total == DefaultApp.Common.Length && DefaultApp.Describe(none).Contains("another app"));
                Choose(".mp4", OpenWith.ProgId);
                Choose(".mp3", @"Applications\MediaPlayerSE.exe");
                Choose(".mkv", "VLC.mkv");
                var some = DefaultApp.Status(root);
                Check("default: Media Player's own ProgId and its Applications entry both count", some.Ours.Count == 2 && some.Ours.Contains(".mp4") && some.Ours.Contains(".mp3") && some.Others.Contains(".mkv"),
                    string.Join(",", some.Ours));
                Check("default: the sentence says how many and which still open elsewhere", DefaultApp.Describe(some).StartsWith("Media Player opens 2 of the") && DefaultApp.Describe(some).Contains(".mkv"), DefaultApp.Describe(some));
                foreach (var ext in DefaultApp.Common) Choose(ext, OpenWith.ProgId);
                var all = DefaultApp.Status(root);
                Check("default: all of them", all.All && DefaultApp.Describe(all).Contains("is your default player"));
                Check("default: the Settings address opens Media Player's own page", DefaultApp.SettingsAddress() == "ms-settings:defaultapps?registeredAppUser=Media%20Player%20%28Sauce%20Edition%29", DefaultApp.SettingsAddress());
                Check("default: the real choices can be read without errors", DefaultApp.Status().Total == DefaultApp.Common.Length);
            }
            finally { Registry.CurrentUser.DeleteSubKeyTree(path, false); }

            string f = System.IO.Path.Combine(Dir("defaultasked"), "settings.json");
            var s = Settings.Load(f); s.DefaultAsked = true; s.Save();
            Check("default: the start page's question is remembered once answered", Settings.Load(f).DefaultAsked);
        }
    }
}
