// Explorer's "Open with": registry entries only, for the person (HKCU\Software\Classes) or for everyone (HKLM, as an
// administrator). It never changes anyone's default app: no UserChoice, no extension's own default value. It writes:
//   Classes\Applications\MediaPlayerSE.exe          FriendlyAppName, DefaultIcon, shell\open, shell\enqueue, SupportedTypes
//   Classes\MediaPlayerSE.File                      the ProgId (DefaultIcon, shell\open, shell\enqueue)
//   Classes\<ext>\OpenWithProgids  MediaPlayerSE.File (an empty value) for every media and playlist extension
//   Software\MediaPlayerSE\Capabilities            ApplicationName, ApplicationDescription, FileAssociations
//   Software\RegisteredApplications  "Media Player (Sauce Edition)" = Software\MediaPlayerSE\Capabilities
// and Unregister removes exactly those (only when they point at this program). The root is passed in, so the checks
// write under HKCU\Software\MediaPlayerSE-Test-<guid> and never under the real Classes key.
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace MediaPlayerSauce.Core
{
    public enum OpenWithState { NotAdded, Added, OtherCopy }

    public static class OpenWith
    {
        public const string AppKey = "MediaPlayerSE.exe";
        public const string ProgId = "MediaPlayerSE.File";
        public const string AppName = "Media Player (Sauce Edition)";
        public const string Description = "Plays videos, music, playlists, discs and streams.";
        public const string CapabilitiesPath = @"Software\MediaPlayerSE\Capabilities";
        const string Classes = @"Software\Classes";

        public static IEnumerable<string> Extensions => MediaFiles.Video.Concat(MediaFiles.Audio).Concat(MediaFiles.Playlists).Distinct(StringComparer.OrdinalIgnoreCase);

        public static string ExePath => Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "MediaPlayerSE.exe");

        public static string OpenCommand(string exe) { return "\"" + exe + "\" \"%1\""; }
        public static string EnqueueCommand(string exe) { return "\"" + exe + "\" --enqueue \"%1\""; }

        static RegistryKey Base(bool allUsers)
        {
            return RegistryKey.OpenBaseKey(allUsers ? RegistryHive.LocalMachine : RegistryHive.CurrentUser, RegistryView.Registry64);
        }

        // ------------------------------------------------------------ for the command line and the dialog
        /// <summary>False when it couldn't be written (for everyone, without being an administrator).</summary>
        public static bool Register(bool allUsers)
        {
            try
            {
                using var root = Base(allUsers);
                Register(root, ExePath);
                Notify();
                return true;
            }
            catch (Exception) { return false; }
        }

        /// <summary>False when it couldn't be removed, or the entries belong to another copy of Media Player.</summary>
        public static bool Unregister(bool allUsers)
        {
            try
            {
                using var root = Base(allUsers);
                bool ok = Unregister(root, ExePath);
                Notify();
                return ok;
            }
            catch (Exception) { return false; }
        }

        public static OpenWithState State(bool allUsers)
        {
            try
            {
                using var root = Base(allUsers);
                return State(root, ExePath);
            }
            catch (Exception) { return OpenWithState.NotAdded; }
        }

        // ------------------------------------------------------------ the work, under any root
        /// <summary>Added for this exe, for another copy of Media Player (another folder), or not at all.</summary>
        public static OpenWithState State(RegistryKey root, string exe)
        {
            string cmd = OurCommand(root);
            if (cmd == null) return OpenWithState.NotAdded;
            return cmd.Equals(OpenCommand(exe), StringComparison.OrdinalIgnoreCase) ? OpenWithState.Added : OpenWithState.OtherCopy;
        }

        static string OurCommand(RegistryKey root)
        {
            return ReadDefault(root, Classes + @"\Applications\" + AppKey + @"\shell\open\command") ?? ReadDefault(root, Classes + @"\" + ProgId + @"\shell\open\command");
        }

        public static void Register(RegistryKey root, string exe)
        {
            string icon = "\"" + exe + "\",0";
            using (var app = root.CreateSubKey(Classes + @"\Applications\" + AppKey))
            {
                app.SetValue("FriendlyAppName", AppName);
                using (var k = app.CreateSubKey("DefaultIcon")) k.SetValue("", icon);
                WriteVerbs(app, exe);
                using var st = app.CreateSubKey("SupportedTypes");
                foreach (var e in Extensions) st.SetValue(e, "");
            }
            using (var pid = root.CreateSubKey(Classes + @"\" + ProgId))
            {
                pid.SetValue("", "Media file");
                pid.SetValue("FriendlyTypeName", "Media file");
                using (var k = pid.CreateSubKey("DefaultIcon")) k.SetValue("", icon);
                WriteVerbs(pid, exe);
            }
            foreach (var e in Extensions)
                using (var k = root.CreateSubKey(Classes + @"\" + e + @"\OpenWithProgids"))
                    k.SetValue(ProgId, Array.Empty<byte>(), RegistryValueKind.None);
            using (var cap = root.CreateSubKey(CapabilitiesPath))
            {
                cap.SetValue("ApplicationName", AppName);
                cap.SetValue("ApplicationDescription", Description);
                cap.SetValue("ApplicationIcon", icon);
                using var fa = cap.CreateSubKey("FileAssociations");
                foreach (var e in Extensions) fa.SetValue(e, ProgId);
            }
            using (var ra = root.CreateSubKey(@"Software\RegisteredApplications"))
                ra.SetValue(AppName, CapabilitiesPath);
        }

        static void WriteVerbs(RegistryKey parent, string exe)
        {
            using (var open = parent.CreateSubKey(@"shell\open"))
            {
                open.SetValue("FriendlyAppName", AppName);
                using var c = open.CreateSubKey("command");
                c.SetValue("", OpenCommand(exe));
            }
            using (var enq = parent.CreateSubKey(@"shell\enqueue"))
            {
                enq.SetValue("", "Add to Media Player's playlist");
                using var c = enq.CreateSubKey("command");
                c.SetValue("", EnqueueCommand(exe));
            }
        }

        /// <summary>Removes what Register wrote. Another copy's entries (a different folder whose MediaPlayerSE.exe still
        /// exists) are left alone and false comes back; entries whose program is gone are removed.</summary>
        public static bool Unregister(RegistryKey root, string exe)
        {
            if (State(root, exe) == OpenWithState.OtherCopy)
            {
                string other = ExeOf(OurCommand(root));
                if (other != null && File.Exists(other)) return false;
            }
            root.DeleteSubKeyTree(Classes + @"\Applications\" + AppKey, false);
            root.DeleteSubKeyTree(Classes + @"\" + ProgId, false);
            foreach (var e in Extensions)
            {
                string owp = Classes + @"\" + e + @"\OpenWithProgids";
                using (var k = root.OpenSubKey(owp, true))
                {
                    if (k == null) continue;
                    if (k.GetValueNames().Contains(ProgId, StringComparer.OrdinalIgnoreCase)) k.DeleteValue(ProgId, false);
                }
                DeleteIfEmpty(root, owp);
                DeleteIfEmpty(root, Classes + @"\" + e);
            }
            using (var ra = root.OpenSubKey(@"Software\RegisteredApplications", true))
                if (ra != null && string.Equals(ra.GetValue(AppName) as string, CapabilitiesPath, StringComparison.OrdinalIgnoreCase)) ra.DeleteValue(AppName, false);
            root.DeleteSubKeyTree(CapabilitiesPath, false);
            DeleteIfEmpty(root, @"Software\MediaPlayerSE");
            return true;
        }

        /// <summary>Deletes a key with no values (not even a default one) and no subkeys.</summary>
        static void DeleteIfEmpty(RegistryKey root, string path)
        {
            try
            {
                using (var k = root.OpenSubKey(path))
                {
                    if (k == null || k.ValueCount > 0 || k.SubKeyCount > 0) return;
                }
                root.DeleteSubKey(path, false);
            }
            catch (Exception) { }
        }

        static string ReadDefault(RegistryKey root, string path)
        {
            using var k = root.OpenSubKey(path);
            return k?.GetValue("") as string;
        }

        static string ExeOf(string command)
        {
            if (string.IsNullOrEmpty(command)) return null;
            command = command.Trim();
            if (command.StartsWith("\""))
            {
                int end = command.IndexOf('"', 1);
                return end > 1 ? command.Substring(1, end - 1) : null;
            }
            int sp = command.IndexOf(' ');
            return sp > 0 ? command.Substring(0, sp) : command;
        }

        // ------------------------------------------------------------ telling Explorer
        [DllImport("shell32.dll")] static extern void SHChangeNotify(int eventId, uint flags, IntPtr item1, IntPtr item2);
        const int SHCNE_ASSOCCHANGED = 0x08000000;

        public static void Notify()
        {
            try { SHChangeNotify(SHCNE_ASSOCCHANGED, 0, IntPtr.Zero, IntPtr.Zero); } catch (Exception) { }
        }
    }
}
