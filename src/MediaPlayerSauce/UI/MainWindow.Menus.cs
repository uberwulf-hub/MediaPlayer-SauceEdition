// The menu bar, the right-click menu on the picture and the small menus of the control bar. Menus that list what the
// file has (tracks, chapters, titles) or what the PC has (sound devices) are filled each time they open.
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using LibVLCSharp.Shared;
using MediaPlayerSauce.Core;
using MediaPlayerSauce.Engine;

namespace MediaPlayerSauce.UI
{
    public partial class MainWindow
    {
        static MenuItem Item(string header, Action click, bool isChecked = false, string gesture = null, bool enabled = true)
        {
            var m = new MenuItem { Header = header, IsChecked = isChecked, InputGestureText = gesture ?? "", IsEnabled = enabled };
            if (click != null) m.Click += (s, e) => click();
            return m;
        }
        static MenuItem Sub(string header, IEnumerable<object> items)
        {
            var m = new MenuItem { Header = header };
            foreach (var i in items) m.Items.Add(i);
            if (m.Items.Count == 0) m.IsEnabled = false;
            return m;
        }
        static void Fill(MenuItem parent, Func<IEnumerable<object>> build)
        {
            parent.Items.Add(new MenuItem { Header = "..." });   // so it shows as a submenu
            parent.SubmenuOpened += (s, e) =>
            {
                if (e.OriginalSource != parent) return;
                parent.Items.Clear();
                foreach (var i in build()) parent.Items.Add(i);
                if (parent.Items.Count == 0) parent.Items.Add(new MenuItem { Header = "Nothing here yet", IsEnabled = false });
            };
        }
        void OpenMenuUnder(FrameworkElement target, IEnumerable<object> items)
        {
            var m = new ContextMenu { PlacementTarget = target, Placement = PlacementMode.Top };
            foreach (var i in items) m.Items.Add(i);
            if (m.Items.Count == 0) m.Items.Add(new MenuItem { Header = "Nothing here yet", IsEnabled = false });
            m.IsOpen = true;
        }

        void WireMenus()
        {
            MnuOpen.Click += (s, e) => OpenFileDialog();
            MnuOpenFolder.Click += (s, e) => OpenFolderDialog();
            MnuOpenDisc.Click += (s, e) => ShowOpenDisc();
            MnuOpenUrl.Click += (s, e) => ShowOpenUrl();
            MnuOnline.Click += (s, e) => ShowOnline(null, null);
            Fill(MnuRecent, BuildRecentItems);
            MnuSavePlaylist.Click += (s, e) => SavePlaylistDialog();
            MnuConvert.Click += (s, e) => ShowConvert();
            MnuQuit.Click += (s, e) => Close();

            MnuPlay.Click += (s, e) => TogglePlay();
            MnuStop.Click += (s, e) => StopPlaying();
            MnuPrev.Click += (s, e) => PlayPrevious();
            MnuNext.Click += (s, e) => PlayNext(true);
            Fill(MnuSpeed, BuildSpeedItems);
            MnuJump.Click += (s, e) => ShowJump();
            MnuBack.Click += (s, e) => Player.SeekBy(-10000);
            MnuFwd.Click += (s, e) => Player.SeekBy(10000);
            MnuFrame.Click += (s, e) => Player.NextFrame();
            MnuAB.Click += (s, e) => CycleAb();
            Fill(MnuTitle, BuildTitleItems);
            Fill(MnuChapter, BuildChapterItems);
            Fill(MnuDiscMenu, BuildDiscMenuItems);
            Fill(MnuLoop, BuildLoopItems);
            MnuShuffle.Click += (s, e) => SetShuffle(MnuShuffle.IsChecked);
            MnuShuffle.Loaded += (s, e) => MnuShuffle.IsChecked = Playlist.Shuffle;

            Fill(MnuAudioTrack, BuildAudioTrackItems);
            Fill(MnuAudioDevice, BuildDeviceItems);
            Fill(MnuStereo, BuildStereoItems);
            Fill(MnuVisual, BuildVisualItems);
            MnuVolUp.Click += (s, e) => ChangeVolume(5);
            MnuVolDown.Click += (s, e) => ChangeVolume(-5);
            MnuMute.Click += (s, e) => ToggleMute();
            MnuNormalize.Click += (s, e) => RebuildEngine(o => o.Normalize = MnuNormalize.IsChecked);
            MnuNormalize.Loaded += (s, e) => MnuNormalize.IsChecked = Player.Options.Normalize;
            MnuAddAudio.Click += (s, e) => AddAudioDialog();

            Fill(MnuVideoTrack, BuildVideoTrackItems);
            MnuFullscreen.Click += (s, e) => ToggleFullscreen();
            Fill(MnuOnTop, BuildOnTopItems);
            MnuFit.Click += (s, e) => FitToVideo();
            Fill(MnuAspect, BuildAspectItems);
            Fill(MnuCrop, BuildCropItems);
            Fill(MnuZoom, BuildZoomItems);
            Fill(MnuDeinterlace, BuildDeinterlaceItems);
            Fill(MnuRotate, BuildRotateItems);
            MnuSnapshot.Click += (s, e) => TakeSnapshot();

            Fill(MnuSubTrack, BuildSubtitleItems);
            MnuAddSub.Click += (s, e) => AddSubtitleDialog();
            MnuSubLater.Click += (s, e) => ShiftSubtitles(100);
            MnuSubSooner.Click += (s, e) => ShiftSubtitles(-100);
            Fill(MnuSubSize, BuildSubSizeItems);

            MnuEffects.Click += (s, e) => ShowEffects(0);
            MnuSync.Click += (s, e) => ShowEffects(3);
            MnuInfo.Click += (s, e) => ShowMediaInfo();
            MnuPrefs.Click += (s, e) => ShowPreferences();

            MnuPlaylist.Click += (s, e) => ShowPlaylistPanel(MnuPlaylist.IsChecked);
            MnuMinimal.Click += (s, e) => SetMinimal(MnuMinimal.IsChecked);
            MnuBigger.Click += (s, e) => ChangeUiScale(0.1);
            MnuSmaller.Click += (s, e) => ChangeUiScale(-0.1);

            MnuKeys.Click += (s, e) => ShowShortcuts();
            MnuOpenWith.Click += (s, e) => ShowOpenWith();
            MnuAbout.Click += (s, e) => ShowAbout();
        }

        IEnumerable<object> BuildRecentItems()
        {
            foreach (var r in Settings.Recent)
            {
                string path = r;
                yield return Item(MediaFiles.DisplayName(r), () => OpenPaths(new[] { path }));
            }
            if (Settings.Recent.Count > 0)
            {
                yield return new Separator();
                yield return Item("Clear the list", () => { Settings.Recent.Clear(); RefreshRecent(); SaveSoon(); });
            }
        }

        IEnumerable<object> BuildSpeedItems()
        {
            float r = Player.Rate;
            foreach (float s in new[] { 0.25f, 0.5f, 0.75f, 1f, 1.25f, 1.5f, 1.75f, 2f, 3f, 4f })
            {
                float v = s;
                yield return Item(s == 1f ? "Normal (1x)" : s.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + "x", () => SetSpeed(v), Math.Abs(r - s) < 0.01f);
            }
            yield return new Separator();
            yield return Item("Faster", () => ChangeSpeed(1), false, "]");
            yield return Item("Slower", () => ChangeSpeed(-1), false, "[");
        }

        IEnumerable<object> BuildLoopItems()
        {
            yield return Item("Off", () => { Playlist.Loop = LoopMode.Off; Settings.Loop = 0; UpdateLoopUi(); SaveSoon(); }, Playlist.Loop == LoopMode.Off);
            yield return Item("The whole playlist", () => { Playlist.Loop = LoopMode.All; Settings.Loop = 1; UpdateLoopUi(); SaveSoon(); }, Playlist.Loop == LoopMode.All);
            yield return Item("This one", () => { Playlist.Loop = LoopMode.One; Settings.Loop = 2; UpdateLoopUi(); SaveSoon(); }, Playlist.Loop == LoopMode.One);
        }

        IEnumerable<object> Tracks(List<TrackChoice> list, int current, Action<int> pick, string none = null)
        {
            foreach (var t in list)
            {
                int id = t.Id;
                string name = id == -1 ? (none ?? "Off") : t.Name;
                yield return Item(name, () => pick(id), id == current);
            }
        }
        IEnumerable<object> BuildAudioTrackItems() => Tracks(Player.AudioTracks(), Player.AudioTrack, id => { Player.AudioTrack = id; ShowOsd("Sound: " + Player.AudioTracks().FirstOrDefault(t => t.Id == id)?.Name); }, "No sound");
        IEnumerable<object> BuildVideoTrackItems() => Tracks(Player.VideoTracks(), Player.VideoTrack, id => Player.VideoTrack = id, "No picture");
        IEnumerable<object> BuildSubtitleItems()
        {
            foreach (var o in Tracks(Player.SubtitleTracks(), Player.SubtitleTrack, id => { Player.SubtitleTrack = id; ShowOsd(id == -1 ? "Subtitles off" : "Subtitles: " + Player.SubtitleTracks().FirstOrDefault(t => t.Id == id)?.Name); })) yield return o;
            yield return new Separator();
            yield return Item("Add a subtitle file...", AddSubtitleDialog);
        }
        /// <summary>The control bar's subtitles button: subtitles, then sound tracks.</summary>
        IEnumerable<object> BuildTrackItems()
        {
            var subs = Player.SubtitleTracks();
            yield return new MenuItem { Header = "SUBTITLES", IsEnabled = false };
            if (subs.Count == 0) yield return new MenuItem { Header = "None in this file", IsEnabled = false };
            foreach (var o in BuildSubtitleItems()) yield return o;
            var audio = Player.AudioTracks();
            if (audio.Count > 2)
            {
                yield return new Separator();
                yield return new MenuItem { Header = "SOUND", IsEnabled = false };
                foreach (var o in BuildAudioTrackItems()) yield return o;
            }
        }

        IEnumerable<object> BuildDeviceItems()
        {
            yield return Item("Windows' default", () => { Settings.AudioDevice = ""; Player.SetAudioDevice(null); SaveSoon(); ShowOsd("Sound: Windows' default"); }, string.IsNullOrEmpty(Settings.AudioDevice));
            foreach (var d in Player.AudioDevices())
            {
                string name = d.Name;
                yield return Item(name, () => { Settings.AudioDevice = name; Player.SetAudioDevice(name); SaveSoon(); ShowOsd("Sound: " + name); }, Settings.AudioDevice == name);
            }
        }

        IEnumerable<object> BuildStereoItems()
        {
            var cur = Player.Stereo;
            (string, AudioOutputChannel)[] modes = { ("Stereo", AudioOutputChannel.Stereo), ("Swap left and right", AudioOutputChannel.RStereo),
                ("Left only", AudioOutputChannel.Left), ("Right only", AudioOutputChannel.Right), ("Surround (Dolby)", AudioOutputChannel.Dolbys) };
            foreach (var (name, ch) in modes)
            {
                var c = ch;
                yield return Item(name, () => { Player.Stereo = c; ShowOsd("Sound: " + name); }, cur == c);
            }
        }

        IEnumerable<object> BuildVisualItems()
        {
            string cur = Player.Options.Visual;
            foreach (var (name, id) in new[] { ("Off", ""), ("Spectrum", "spectrum"), ("Wave", "scope"), ("Level meter", "vuMeter"), ("Spectrum and wave", "visual"), ("Swirls (Goom)", "goom") })
            {
                string v = id;
                yield return Item(name, () => { RebuildEngine(o => o.Visual = v); UpdateAudioPanel(); }, cur == id);
            }
        }

        IEnumerable<object> BuildOnTopItems()
        {
            yield return Item("Never", () => SetOnTop("never"), Settings.OnTop == "never");
            yield return Item("Always", () => SetOnTop("always"), Settings.OnTop == "always");
            yield return Item("While playing", () => SetOnTop("playing"), Settings.OnTop == "playing");
        }
        void SetOnTop(string v) { Settings.OnTop = v; UpdateOnTop(); SaveSoon(); }

        IEnumerable<object> BuildAspectItems()
        {
            foreach (var (name, v) in new[] { ("The video's own", ""), ("16:9", "16:9"), ("4:3", "4:3"), ("21:9 (cinema)", "21:9"), ("2.35:1", "235:100"), ("16:10", "16:10"), ("1:1 (square)", "1:1"), ("5:4", "5:4") })
            {
                string a = v;
                yield return Item(name, () => SetAspect(a), Aspect == v);
            }
        }
        IEnumerable<object> BuildCropItems()
        {
            foreach (var (name, v) in new[] { ("None", ""), ("16:9", "16:9"), ("4:3", "4:3"), ("21:9", "21:9"), ("2.35:1", "235:100"), ("16:10", "16:10"), ("1:1", "1:1") })
            {
                string c = v;
                yield return Item(name, () => SetCrop(c), CropValue == v);
            }
        }
        IEnumerable<object> BuildZoomItems()
        {
            foreach (var (name, v) in new[] { ("Fit the window", 0f), ("Quarter size", 0.25f), ("Half size", 0.5f), ("Actual size", 1f), ("Double size", 2f) })
            {
                float z = v;
                yield return Item(name, () => SetZoom(z), Math.Abs(ZoomValue - v) < 0.01f);
            }
        }
        IEnumerable<object> BuildDeinterlaceItems()
        {
            foreach (var (name, v) in new[] { ("Off", ""), ("Blend", "blend"), ("Bob", "bob"), ("Linear", "linear"), ("Mean", "mean"), ("Yadif (best)", "yadif"), ("Yadif 2x (smoothest)", "yadif2x"), ("Discard", "discard"), ("X", "x") })
            {
                string d = v;
                yield return Item(name, () => SetDeinterlace(d), DeinterlaceValue == v);
            }
        }
        IEnumerable<object> BuildRotateItems()
        {
            string[] names = { "None", "Turn 90° right", "Turn 180°", "Turn 90° left", "Flip sideways", "Flip upside down" };
            for (int i = 0; i < names.Length; i++)
            {
                int r = i;
                yield return Item(names[i], () => SetRotation(r), Rotation == i);
            }
        }

        IEnumerable<object> BuildSubSizeItems()
        {
            int cur = Player.Options.SubtitleScale;
            foreach (var (name, v) in new[] { ("Small", 75), ("Normal", 100), ("Large", 130), ("Larger", 160), ("Huge", 200) })
            {
                int s = v;
                yield return Item(name, () => { RebuildEngine(o => o.SubtitleScale = s); ShowOsd("Subtitle size: " + name); }, cur == v);
            }
        }

        public void ShiftSubtitles(long ms)
        {
            Player.SubtitleDelayMs += ms;
            ShowOsd("Subtitles " + DelayText(Player.SubtitleDelayMs));
        }
        public void ShiftAudio(long ms)
        {
            Player.AudioDelayMs += ms;
            ShowOsd("Sound " + DelayText(Player.AudioDelayMs));
        }
        public static string DelayText(long ms)
        {
            if (ms == 0) return "in step";
            return (Math.Abs(ms) / 1000.0).ToString("0.0#", System.Globalization.CultureInfo.InvariantCulture) + " s " + (ms > 0 ? "later" : "sooner");
        }

        IEnumerable<object> BuildTitleItems()
        {
            int cur = Player.Title;
            foreach (var t in Player.Titles())
            {
                int id = t.Id;
                yield return Item(t.Name, () => Player.Title = id, id == cur);
            }
        }
        IEnumerable<object> BuildChapterItems()
        {
            int cur = Player.Chapter;
            var list = Player.Chapters();
            if (list.Count > 0)
            {
                yield return Item("Next chapter", Player.NextChapter, false, "Shift+N");
                yield return Item("Previous chapter", Player.PreviousChapter, false, "Shift+P");
                yield return new Separator();
            }
            foreach (var c in list)
            {
                int id = c.Id;
                yield return Item(c.Name, () => Player.Chapter = id, id == cur);
            }
        }
        IEnumerable<object> BuildDiscMenuItems()
        {
            bool disc = (Player.CurrentPath ?? "").StartsWith("dvd", StringComparison.OrdinalIgnoreCase) || (Player.CurrentPath ?? "").StartsWith("bluray", StringComparison.OrdinalIgnoreCase);
            yield return Item("Go to the disc's menu", () => { Player.Title = 0; }, false, null, disc);
            yield return Item("Select", () => Player.Navigate(0), false, "Enter", disc);
            yield return Item("Up", () => Player.Navigate(1), false, null, disc);
            yield return Item("Down", () => Player.Navigate(2), false, null, disc);
            yield return Item("Left", () => Player.Navigate(3), false, null, disc);
            yield return Item("Right", () => Player.Navigate(4), false, null, disc);
            yield return Item("Pop-up menu (Blu-ray)", () => Player.Navigate(5), false, null, disc);
        }

        ContextMenu BuildPlaylistMenu()
        {
            var m = new ContextMenu();
            var sel = PlaylistList.SelectedItems.Cast<PlaylistItem>().ToList();
            m.Items.Add(Item("Play", () => { if (sel.Count > 0) PlayItem(sel[0]); }, false, "Enter", sel.Count > 0));
            m.Items.Add(Item("Remove", RemoveSelected, false, "Delete", sel.Count > 0));
            m.Items.Add(Item("Show in File Explorer", () => ShowInExplorer(sel[0].Path), false, null, sel.Count == 1 && !sel[0].IsUrl));
            m.Items.Add(new Separator());
            m.Items.Add(Item("Add files...", () => OpenFileDialog(false)));
            m.Items.Add(Item("Add a folder...", OpenFolderDialog));
            m.Items.Add(Sub("Sort by", new object[]
            {
                Item("Name", () => Playlist.Sort((a, b) => MediaFiles.NaturalCompare(a.Title, b.Title))),
                Item("File name and folder", () => Playlist.Sort((a, b) => MediaFiles.NaturalCompare(a.Path, b.Path))),
                Item("Length", () => Playlist.Sort((a, b) => a.LengthMs.CompareTo(b.LengthMs))),
                Item("Mix them up", () => { var r = new Random(); Playlist.Sort((a, b) => 0); var shuffled = Playlist.Items.OrderBy(_ => r.Next()).ToList(); var cur = Playlist.Current; Playlist.Items.Clear(); foreach (var it in shuffled) Playlist.Items.Add(it); if (cur != null) Playlist.Select(Playlist.Items.IndexOf(cur)); }),
            }));
            m.Items.Add(Item("Save the playlist...", SavePlaylistDialog, false, "Ctrl+Y", Playlist.Count > 0));
            m.Items.Add(Item("Clear the playlist", () => { Player.Stop(); Playlist.Clear(); Welcome.Visibility = Visibility.Visible; AudioPanel.Visibility = Visibility.Collapsed; Title = "Media Player (Sauce Edition)"; }, false, null, Playlist.Count > 0));
            return m;
        }

        static void ShowInExplorer(string path)
        {
            try { System.Diagnostics.Process.Start("explorer.exe", "/select,\"" + path + "\""); } catch (Exception) { }
        }

        /// <summary>The right-click menu on the picture: the most used things, then the menu bar's menus.</summary>
        ContextMenu BuildContextMenu()
        {
            var m = new ContextMenu();
            m.Items.Add(Item(Player.IsPlaying ? "Pause" : "Play", TogglePlay, false, "Space"));
            m.Items.Add(Item("Stop", StopPlaying, false, "S"));
            m.Items.Add(Item("Previous", PlayPrevious, false, "P", Playlist.Count > 1));
            m.Items.Add(Item("Next", () => PlayNext(true), false, "N", Playlist.Count > 1));
            m.Items.Add(new Separator());
            m.Items.Add(Item(IsFullscreen ? "Leave full screen" : "Full screen", ToggleFullscreen, false, "F"));
            m.Items.Add(Sub("Speed", BuildSpeedItems()));
            m.Items.Add(Sub("Sound track", BuildAudioTrackItems()));
            m.Items.Add(Sub("Subtitles", BuildSubtitleItems()));
            m.Items.Add(Sub("Aspect ratio", BuildAspectItems()));
            m.Items.Add(Sub("Crop", BuildCropItems()));
            m.Items.Add(Sub("Zoom", BuildZoomItems()));
            if (Player.ChapterCount > 1) m.Items.Add(Sub("Chapter", BuildChapterItems()));
            m.Items.Add(new Separator());
            m.Items.Add(Item("Take a snapshot", TakeSnapshot, false, "Shift+S"));
            m.Items.Add(Item("Effects and filters...", () => ShowEffects(0), false, "Ctrl+E"));
            m.Items.Add(Item("Media information...", ShowMediaInfo, false, "Ctrl+I"));
            m.Items.Add(new Separator());
            m.Items.Add(Item("Open file...", () => OpenFileDialog(), false, "Ctrl+O"));
            m.Items.Add(Item("Playlist", () => ShowPlaylistPanel(!Settings.ShowPlaylist), Settings.ShowPlaylist, "Ctrl+L"));
            m.Items.Add(Item("Minimal view", () => SetMinimal(!MnuMinimal.IsChecked), MnuMinimal.IsChecked, "Ctrl+H"));
            return m;
        }
    }
}
