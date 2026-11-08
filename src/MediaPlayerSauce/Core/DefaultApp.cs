// Making Media Player the default player. Windows doesn't let a program make itself the default (the choice is
// protected, and only the person can make it in Settings), so this only reads which app opens each common kind of
// file now (the UserChoice ProgId, read only) and gives the address of Media Player's own page in Windows'
// Default apps, where Windows 11 has one "Set default" button for all of them.
using Microsoft.Win32;

namespace MediaPlayerSauce.Core
{
    public sealed class DefaultStatus
    {
        public List<string> Ours = new List<string>();      // kinds that open with Media Player now
        public List<string> Others = new List<string>();    // kinds that open with another app (or none chosen)
        public int Total => Ours.Count + Others.Count;
        public bool All => Others.Count == 0 && Ours.Count > 0;
        public bool None => Ours.Count == 0;
    }

    public static class DefaultApp
    {
        /// <summary>The kinds of files people mean by "my videos and music".</summary>
        public static readonly string[] Common = { ".mp4", ".mkv", ".avi", ".mov", ".wmv", ".webm", ".m4v", ".mpg", ".mp3", ".m4a", ".flac", ".wav", ".wma", ".ogg", ".aac" };

        const string FileExts = @"Software\Microsoft\Windows\CurrentVersion\Explorer\FileExts";

        /// <summary>The ProgId Windows opens ext with for this person ("" when they haven't chosen one).</summary>
        public static string ChosenProgId(RegistryKey hkcu, string ext)
        {
            try
            {
                using var k = hkcu.OpenSubKey(FileExts + "\\" + ext + "\\UserChoice");
                return k?.GetValue("ProgId") as string ?? "";
            }
            catch (Exception) { return ""; }
        }

        public static bool IsOurs(string progId)
        {
            return string.Equals(progId, OpenWith.ProgId, StringComparison.OrdinalIgnoreCase)
                || string.Equals(progId, "Applications\\" + OpenWith.AppKey, StringComparison.OrdinalIgnoreCase);
        }

        public static DefaultStatus Status(RegistryKey hkcu = null)
        {
            hkcu ??= Registry.CurrentUser;
            var s = new DefaultStatus();
            foreach (var ext in Common) (IsOurs(ChosenProgId(hkcu, ext)) ? s.Ours : s.Others).Add(ext);
            return s;
        }

        /// <summary>Media Player's own page in Windows' Default apps (Windows 11; Windows 10 opens the Default apps page,
        /// where the Video player and Music player can be set).</summary>
        public static string SettingsAddress()
        {
            return "ms-settings:defaultapps?registeredAppUser=" + Uri.EscapeDataString(OpenWith.AppName);
        }

        public static bool IsWindows11 => Environment.OSVersion.Version.Build >= 22000;

        /// <summary>A sentence for the person about where things stand.</summary>
        public static string Describe(DefaultStatus s)
        {
            if (s.All) return "Media Player is your default player for videos and music.";
            if (s.None) return "Your videos and music open with another app at the moment.";
            return "Media Player opens " + s.Ours.Count + " of the " + s.Total + " common kinds of video and music files. Still opening elsewhere: "
                + string.Join(", ", s.Others) + ".";
        }
    }
}
