// Keyboard shortcuts, the same as VLC's where VLC has one (Help > Keyboard shortcuts lists them all: Shortcuts.All).
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Controls.Primitives;

namespace MediaPlayerSauce.UI
{
    public sealed class Shortcut
    {
        public string Keys { get; set; }
        public string What { get; set; }
        public string Group { get; set; }
    }

    public partial class MainWindow
    {
        void WireKeys()
        {
            PreviewKeyDown += OnKey;
        }

        void OnKey(object sender, KeyEventArgs e)
        {
            // typing in a box (the playlist filter, a search) keeps its keys
            if (Keyboard.FocusedElement is TextBox && e.Key != Key.Escape) return;
            if (e.OriginalSource is MenuItem || Keyboard.FocusedElement is ComboBox) return;
            Key k = e.Key == Key.System ? e.SystemKey : e.Key;
            var mods = Keyboard.Modifiers;
            bool ctrl = mods.HasFlag(ModifierKeys.Control), shift = mods.HasFlag(ModifierKeys.Shift), alt = mods.HasFlag(ModifierKeys.Alt);
            if (Keyboard.FocusedElement is ListBoxItem && (k == Key.Up || k == Key.Down || k == Key.Enter || k == Key.Delete || k == Key.Home || k == Key.End || k == Key.PageUp || k == Key.PageDown) && !ctrl) return;
            if (HandleKey(k, ctrl, shift, alt)) e.Handled = true;
        }

        /// <summary>Runs the shortcut for a key; false when the key isn't one.</summary>
        public bool HandleKey(Key k, bool ctrl, bool shift, bool alt)
        {
            if (ctrl && !shift && !alt)
            {
                switch (k)
                {
                    case Key.O: OpenFileDialog(); return true;
                    case Key.F: OpenFolderDialog(); return true;
                    case Key.D: ShowOpenDisc(); return true;
                    case Key.N: ShowOpenUrl(); return true;
                    case Key.Y: SavePlaylistDialog(); return true;
                    case Key.R: ShowConvert(); return true;
                    case Key.Q: Close(); return true;
                    case Key.T: ShowJump(); return true;
                    case Key.E: ShowEffects(0); return true;
                    case Key.I: ShowMediaInfo(); return true;
                    case Key.P: ShowPreferences(); return true;
                    case Key.L: ShowPlaylistPanel(!Settings.ShowPlaylist); return true;
                    case Key.H: SetMinimal(!MnuMinimal.IsChecked); return true;
                    case Key.Up: ChangeVolume(5); return true;
                    case Key.Down: ChangeVolume(-5); return true;
                    case Key.Left: Player.SeekBy(-60000); ShowOsd("Back 1 minute"); return true;
                    case Key.Right: Player.SeekBy(60000); ShowOsd("Forward 1 minute"); return true;
                    case Key.OemPlus: case Key.Add: ChangeUiScale(0.1); return true;
                    case Key.OemMinus: case Key.Subtract: ChangeUiScale(-0.1); return true;
                    case Key.D0: case Key.NumPad0: Settings.UiScale = 1; ChangeUiScale(0); return true;
                }
                return false;
            }
            if (ctrl && shift)
            {
                if (k == Key.O) { OpenFolderDialog(); return true; }
                return false;
            }
            if (alt)
            {
                switch (k)
                {
                    case Key.Left: Player.SeekBy(-10000); ShowOsd("Back 10 seconds"); return true;
                    case Key.Right: Player.SeekBy(10000); ShowOsd("Forward 10 seconds"); return true;
                    case Key.Enter: ToggleFullscreen(); return true;
                }
                return false;
            }
            if (shift)
            {
                switch (k)
                {
                    case Key.Left: Player.SeekBy(-3000); ShowOsd("Back 3 seconds"); return true;
                    case Key.Right: Player.SeekBy(3000); ShowOsd("Forward 3 seconds"); return true;
                    case Key.S: TakeSnapshot(); return true;
                    case Key.L: CycleAb(); return true;
                    case Key.N: Player.NextChapter(); ShowOsd("Next chapter"); return true;
                    case Key.P: Player.PreviousChapter(); ShowOsd("Previous chapter"); return true;
                    case Key.OemPlus: case Key.Add: ChangeSpeed(1); return true;
                    case Key.OemMinus: case Key.Subtract: ChangeSpeed(-1); return true;
                }
                return false;
            }
            switch (k)
            {
                case Key.Space: case Key.MediaPlayPause: case Key.Play: TogglePlay(); return true;
                case Key.MediaStop: case Key.S: StopPlaying(); return true;
                case Key.MediaNextTrack: case Key.N: PlayNext(true); return true;
                case Key.MediaPreviousTrack: case Key.P: PlayPrevious(); return true;
                case Key.F: case Key.F11: ToggleFullscreen(); return true;
                case Key.Escape:
                    if (IsFullscreen) { SetFullscreen(false); return true; }
                    if (Notice.Visibility == Visibility.Visible) { Notice.Visibility = Visibility.Collapsed; return true; }
                    return false;
                case Key.Left: Player.SeekBy(-10000); ShowOsd("Back 10 seconds"); return true;
                case Key.Right: Player.SeekBy(10000); ShowOsd("Forward 10 seconds"); return true;
                case Key.Up: case Key.VolumeUp: ChangeVolume(5); return true;
                case Key.Down: case Key.VolumeDown: ChangeVolume(-5); return true;
                case Key.M: case Key.VolumeMute: ToggleMute(); return true;
                case Key.OemOpenBrackets: case Key.Subtract: ChangeSpeed(-1); return true;
                case Key.OemCloseBrackets: case Key.Add: ChangeSpeed(1); return true;
                case Key.OemPlus: SetSpeed(1f); return true;
                case Key.E: Player.NextFrame(); return true;
                case Key.T: ShowOsd(MediaFilesTimeText(), 2.5); return true;
                case Key.Home: if (Player.HasMedia) { Player.Time = 0; ShowOsd("From the start"); } return true;
                case Key.End: if (Player.HasMedia && Player.Length > 0) Player.Time = Player.Length - 5000; return true;
                case Key.L: CycleLoop(); return true;
                case Key.R: SetShuffle(!Playlist.Shuffle); return true;
                case Key.A: CycleIn(BuildAspectValues(), Aspect, SetAspect); return true;
                case Key.C: CycleIn(new[] { "", "16:9", "4:3", "21:9", "235:100", "16:10", "1:1" }, CropValue, SetCrop); return true;
                case Key.Z: { float[] z = { 0f, 0.25f, 0.5f, 1f, 2f }; int i = Array.FindIndex(z, v => Math.Abs(v - ZoomValue) < 0.01f); SetZoom(z[(i + 1) % z.Length]); return true; }
                case Key.D: SetDeinterlace(DeinterlaceValue.Length == 0 ? "yadif" : ""); return true;
                case Key.V: CycleTrack(Player.SubtitleTracks(), Player.SubtitleTrack, id => Player.SubtitleTrack = id, "Subtitles"); return true;
                case Key.B: CycleTrack(Player.AudioTracks(), Player.AudioTrack, id => Player.AudioTrack = id, "Sound"); return true;
                case Key.G: ShiftSubtitles(-100); return true;
                case Key.H: ShiftSubtitles(100); return true;
                case Key.J: ShiftAudio(-50); return true;
                case Key.K: ShiftAudio(50); return true;
                case Key.I: ShowMediaInfo(); return true;
                case Key.F1: ShowShortcuts(); return true;
                case Key.Oem2: case Key.Divide: ShowOnline(null, null); return true;   // the / key
                case Key.D1: case Key.D2: case Key.D3: case Key.D4: case Key.D5: case Key.D6: case Key.D7: case Key.D8: case Key.D9: case Key.D0:
                    // 1-9 jump to 10%..90% of the way through, 0 to the start (as on many video sites)
                    if (Player.HasMedia && Player.Length > 0) { int n = k == Key.D0 ? 0 : k - Key.D0; Player.Time = Player.Length * n / 10; ShowOsd(n * 10 + "%"); }
                    return true;
            }
            return false;
        }

        string MediaFilesTimeText()
        {
            long t = Player.Time, l = Player.Length;
            return Core.MediaFiles.Time(t) + (l > 0 ? " / " + Core.MediaFiles.Time(l) + "   (" + Core.MediaFiles.Time(Math.Max(0, l - t)) + " left)" : "");
        }

        static string[] BuildAspectValues() => new[] { "", "16:9", "4:3", "21:9", "235:100", "16:10", "1:1", "5:4" };
        static void CycleIn(string[] values, string cur, Action<string> set)
        {
            int i = Array.IndexOf(values, cur ?? "");
            set(values[(i + 1) % values.Length]);
        }
        void CycleTrack(List<Engine.TrackChoice> list, int cur, Action<int> set, string what)
        {
            if (list.Count == 0) { ShowOsd(what + ": none"); return; }
            int i = list.FindIndex(t => t.Id == cur);
            var next = list[(i + 1) % list.Count];
            set(next.Id);
            ShowOsd(what + ": " + (next.Id == -1 ? "off" : next.Name));
        }

        /// <summary>Everything Help > Keyboard shortcuts lists.</summary>
        public static readonly Shortcut[] AllShortcuts =
        {
            new Shortcut { Group = "Playing", Keys = "Space", What = "Play or pause" },
            new Shortcut { Group = "Playing", Keys = "S", What = "Stop" },
            new Shortcut { Group = "Playing", Keys = "N / P", What = "Next / previous in the playlist" },
            new Shortcut { Group = "Playing", Keys = "Left / Right", What = "Back / forward 10 seconds" },
            new Shortcut { Group = "Playing", Keys = "Shift+Left / Right", What = "Back / forward 3 seconds" },
            new Shortcut { Group = "Playing", Keys = "Ctrl+Left / Right", What = "Back / forward 1 minute" },
            new Shortcut { Group = "Playing", Keys = "1 ... 9, 0", What = "Jump to 10% ... 90%, or the start" },
            new Shortcut { Group = "Playing", Keys = "Home / End", What = "The start / near the end" },
            new Shortcut { Group = "Playing", Keys = "Ctrl+T", What = "Jump to a time" },
            new Shortcut { Group = "Playing", Keys = "[ / ]", What = "Slower / faster" },
            new Shortcut { Group = "Playing", Keys = "=", What = "Normal speed" },
            new Shortcut { Group = "Playing", Keys = "E", What = "Next frame" },
            new Shortcut { Group = "Playing", Keys = "Shift+L", What = "A-B repeat (press for A, again for B, again to stop)" },
            new Shortcut { Group = "Playing", Keys = "L", What = "Loop: off, the whole playlist, this one" },
            new Shortcut { Group = "Playing", Keys = "R", What = "Shuffle on or off" },
            new Shortcut { Group = "Playing", Keys = "Shift+N / Shift+P", What = "Next / previous chapter" },
            new Shortcut { Group = "Playing", Keys = "T", What = "Show the time" },
            new Shortcut { Group = "Sound", Keys = "Up / Down, wheel", What = "Volume up / down (up to 200%)" },
            new Shortcut { Group = "Sound", Keys = "M", What = "Mute" },
            new Shortcut { Group = "Sound", Keys = "B", What = "Next sound track" },
            new Shortcut { Group = "Sound", Keys = "J / K", What = "Sound sooner / later" },
            new Shortcut { Group = "Picture", Keys = "F, double-click", What = "Full screen" },
            new Shortcut { Group = "Picture", Keys = "Esc", What = "Leave full screen" },
            new Shortcut { Group = "Picture", Keys = "A", What = "Next aspect ratio" },
            new Shortcut { Group = "Picture", Keys = "C", What = "Next crop" },
            new Shortcut { Group = "Picture", Keys = "Z", What = "Next zoom" },
            new Shortcut { Group = "Picture", Keys = "D", What = "Deinterlace on or off" },
            new Shortcut { Group = "Picture", Keys = "Shift+S", What = "Save a snapshot (Pictures\\Media Player)" },
            new Shortcut { Group = "Subtitles", Keys = "V", What = "Next subtitle track" },
            new Shortcut { Group = "Subtitles", Keys = "G / H", What = "Subtitles sooner / later" },
            new Shortcut { Group = "Opening", Keys = "Ctrl+O", What = "Open files" },
            new Shortcut { Group = "Opening", Keys = "Ctrl+F", What = "Open a folder" },
            new Shortcut { Group = "Opening", Keys = "Ctrl+D", What = "Open a disc" },
            new Shortcut { Group = "Opening", Keys = "Ctrl+N", What = "Open a network stream" },
            new Shortcut { Group = "Opening", Keys = "/", What = "Watch online" },
            new Shortcut { Group = "Opening", Keys = "Ctrl+Y", What = "Save the playlist" },
            new Shortcut { Group = "Opening", Keys = "Ctrl+R", What = "Convert or save a copy" },
            new Shortcut { Group = "Window", Keys = "Ctrl+L", What = "Show or hide the playlist" },
            new Shortcut { Group = "Window", Keys = "Ctrl+H", What = "Minimal view (no menu)" },
            new Shortcut { Group = "Window", Keys = "Ctrl+E", What = "Effects and filters" },
            new Shortcut { Group = "Window", Keys = "Ctrl+I", What = "Media information" },
            new Shortcut { Group = "Window", Keys = "Ctrl+P", What = "Preferences" },
            new Shortcut { Group = "Window", Keys = "Ctrl+Plus / Minus / 0", What = "Bigger / smaller / normal interface" },
            new Shortcut { Group = "Window", Keys = "F1", What = "This list" },
            new Shortcut { Group = "Window", Keys = "Ctrl+Q", What = "Quit" },
        };
    }
}
