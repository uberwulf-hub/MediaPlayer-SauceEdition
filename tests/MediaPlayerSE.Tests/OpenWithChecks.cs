// Explorer's "Open with" written into HKCU\Software\MediaPlayerSE-Test-<guid> (deleted afterwards), never into the
// real Software\Classes or HKLM.
using System.IO;
using MediaPlayerSauce.Core;
using Microsoft.Win32;

namespace MediaPlayerSE.Tests
{
    static partial class Program
    {
        static void OpenWithChecks()
        {
            string testPath = @"Software\MediaPlayerSE-Test-" + Guid.NewGuid().ToString("N");
            string exe = Path.Combine(Temp, "app", "MediaPlayerSE.exe");
            Directory.CreateDirectory(Path.GetDirectoryName(exe));
            File.WriteAllText(exe, "not really");
            try
            {
                using var root = Registry.CurrentUser.CreateSubKey(testPath);
                const string C = @"Software\Classes";
                // something already there that isn't ours
                using (var k = root.CreateSubKey(C + @"\.mp4")) k.SetValue("", "Other.File");
                using (var k = root.CreateSubKey(C + @"\.mp4\OpenWithProgids")) k.SetValue("Other.File", Array.Empty<byte>(), RegistryValueKind.None);
                using (var k = root.CreateSubKey(@"Software\RegisteredApplications")) k.SetValue("Other App", @"Software\Other\Capabilities");

                Check("openwith: not added at first", OpenWith.State(root, exe) == OpenWithState.NotAdded);
                OpenWith.Register(root, exe);
                Check("openwith: added", OpenWith.State(root, exe) == OpenWithState.Added);

                string Def(string path) { using var k = root.OpenSubKey(path); return k?.GetValue("") as string; }
                string Val(string path, string name) { using var k = root.OpenSubKey(path); return k?.GetValue(name) as string; }
                string app = C + @"\Applications\MediaPlayerSE.exe";
                Check("openwith: the application's name", Val(app, "FriendlyAppName") == "Media Player (Sauce Edition)");
                Check("openwith: open command", Def(app + @"\shell\open\command") == "\"" + exe + "\" \"%1\"", Def(app + @"\shell\open\command"));
                Check("openwith: add-to-playlist verb", Def(app + @"\shell\enqueue") == "Add to Media Player's playlist" && Def(app + @"\shell\enqueue\command") == "\"" + exe + "\" --enqueue \"%1\"");
                using (var st = root.OpenSubKey(app + @"\SupportedTypes"))
                    Check("openwith: every media and playlist type is supported", st != null && OpenWith.Extensions.All(e => st.GetValueNames().Contains(e)) && st.ValueCount == OpenWith.Extensions.Count()
                        && st.GetValueNames().Contains(".mkv") && st.GetValueNames().Contains(".flac") && st.GetValueNames().Contains(".m3u8"), st?.ValueCount.ToString());
                Check("openwith: the ProgId with its icon and command", Def(C + @"\MediaPlayerSE.File\DefaultIcon") == "\"" + exe + "\",0" && Def(C + @"\MediaPlayerSE.File\shell\open\command") == "\"" + exe + "\" \"%1\"");
                bool allProgids = OpenWith.Extensions.All(e =>
                {
                    using var k = root.OpenSubKey(C + @"\" + e + @"\OpenWithProgids");
                    return k != null && k.GetValueNames().Contains("MediaPlayerSE.File") && k.GetValueKind("MediaPlayerSE.File") == RegistryValueKind.None;
                });
                Check("openwith: OpenWithProgids for every extension", allProgids);
                string cap = @"Software\MediaPlayerSE\Capabilities";
                Check("openwith: Default apps capabilities", Val(cap, "ApplicationName") == "Media Player (Sauce Edition)" && !string.IsNullOrEmpty(Val(cap, "ApplicationDescription"))
                    && Val(cap + @"\FileAssociations", ".mp4") == "MediaPlayerSE.File" && Val(cap + @"\FileAssociations", ".mp3") == "MediaPlayerSE.File");
                Check("openwith: listed in RegisteredApplications", Val(@"Software\RegisteredApplications", "Media Player (Sauce Edition)") == cap);

                // never the person's default app
                var allKeys = new List<string>();
                void Walk(RegistryKey k, string path) { allKeys.Add(path); foreach (var n in k.GetSubKeyNames()) using (var s = k.OpenSubKey(n)) Walk(s, path + "\\" + n); }
                Walk(root, "");
                Check("openwith: no UserChoice anywhere", !allKeys.Any(p => p.EndsWith("\\UserChoice", StringComparison.OrdinalIgnoreCase)));
                bool noDefault = OpenWith.Extensions.Where(e => e != ".mp4").All(e => Def(C + @"\" + e) == null);
                Check("openwith: no extension's default value written; .mp4 still opens with what it did", noDefault && Def(C + @"\.mp4") == "Other.File");
                Check("openwith: registering twice changes nothing", TryOw(() => OpenWith.Register(root, exe)) && OpenWith.State(root, exe) == OpenWithState.Added);

                // another copy's entries are left alone while that copy exists
                string other = Path.Combine(Temp, "other", "MediaPlayerSE.exe");
                Directory.CreateDirectory(Path.GetDirectoryName(other));
                File.WriteAllText(other, "x");
                Check("openwith: another copy is told apart", OpenWith.State(root, other) == OpenWithState.OtherCopy);
                Check("openwith: another copy can't remove this one's entries", !OpenWith.Unregister(root, other) && OpenWith.State(root, exe) == OpenWithState.Added);

                Check("openwith: removed", OpenWith.Unregister(root, exe) && OpenWith.State(root, exe) == OpenWithState.NotAdded);
                allKeys.Clear();
                Walk(root, "");
                var leftovers = allKeys.Where(p => p.Contains("MediaPlayerSE", StringComparison.OrdinalIgnoreCase)).ToList();
                bool leftValue = false;
                foreach (var p in allKeys)
                {
                    using var k = p.Length == 0 ? root.OpenSubKey("") : root.OpenSubKey(p.TrimStart('\\'));
                    if (k == null) continue;
                    foreach (var n in k.GetValueNames())
                        if (n.Contains("MediaPlayerSE", StringComparison.OrdinalIgnoreCase) || n == "Media Player (Sauce Edition)" || (k.GetValue(n) as string ?? "").Contains(exe, StringComparison.OrdinalIgnoreCase)) leftValue = true;
                }
                Check("openwith: nothing of ours left behind", leftovers.Count == 0 && !leftValue, string.Join(", ", leftovers));
                Check("openwith: no empty keys left behind (only the .mp4 that was there)", root.OpenSubKey(C + @"\.mkv") == null && root.OpenSubKey(@"Software\MediaPlayerSE") == null && root.OpenSubKey(C + @"\Applications") != null);
                using (var k = root.OpenSubKey(C + @"\.mp4\OpenWithProgids"))
                    Check("openwith: what was there before is untouched", Def(C + @"\.mp4") == "Other.File" && k != null && k.GetValueNames().SequenceEqual(new[] { "Other.File" })
                        && Val(@"Software\RegisteredApplications", "Other App") == @"Software\Other\Capabilities");

                // entries left by a copy that has since been deleted are cleaned up
                string gone = Path.Combine(Temp, "gone", "MediaPlayerSE.exe");
                OpenWith.Register(root, gone);
                Check("openwith: a deleted copy's entries can be removed by this copy", OpenWith.Unregister(root, exe) && OpenWith.State(root, exe) == OpenWithState.NotAdded);
                Check("openwith: removing when nothing is there is fine", TryOw(() => OpenWith.Unregister(root, exe)));
            }
            finally
            {
                try { Registry.CurrentUser.DeleteSubKeyTree(testPath, false); } catch (Exception) { }
            }
            using (var k = Registry.CurrentUser.OpenSubKey(testPath))
                Check("openwith: the test key is gone afterwards", k == null);
        }

        static bool TryOw(Action a) { try { a(); return true; } catch (Exception) { return false; } }
    }
}
