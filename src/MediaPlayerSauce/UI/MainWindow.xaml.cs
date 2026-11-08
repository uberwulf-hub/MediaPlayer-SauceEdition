// The main window: the picture, the controls, the playlist and everything that ties them to the engine. Menus are in
// MainWindow.Menus.cs, the keyboard in MainWindow.Keys.cs, dialogs in their own files.
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using LibVLCSharp.Shared;
using MediaPlayerSauce.Core;
using MediaPlayerSauce.Engine;

namespace MediaPlayerSauce.UI
{
    public sealed class RecentEntry
    {
        public string Path { get; set; }
        public string Name { get; set; }
        public string Glyph { get; set; }
        public string Note { get; set; }
    }

    public partial class MainWindow : Window
    {
        public Settings Settings { get; }
        public Player Player { get; private set; }
        public Playlist Playlist { get; } = new Playlist();
        /// <summary>True for --snapshot runs: no window on the screen, no sound, no saving.</summary>
        public bool Quiet { get; }

        readonly DispatcherTimer tick, hideTimer, osdTimer, saveTimer;
        long length = -1, lastTime;
        bool seeking, showRemaining, fullscreen, minimal;
        long abA = -1, abB = -1;
        WindowState stateBeforeFull;
        WindowStyle styleBeforeFull;
        ResizeMode resizeBeforeFull;
        string aspect = "", crop = "", deinterlace = "";
        int rotate;   // 0 none, 1 90, 2 180, 3 270, 4 flip sideways, 5 flip upside down
        float zoom;

        public MainWindow(Settings settings, bool quiet)
        {
            Settings = settings;
            Quiet = quiet;
            InitializeComponent();

            Player = new Player(new EngineOptions { SubtitleScale = settings.SubtitleScale, Normalize = settings.Normalize, Visual = settings.Visual, HardwareDecoding = settings.HardwareDecoding, Headless = quiet },
                                Engine.Player.DispatcherPost(Dispatcher));
            HookPlayer();

            if (quiet)
            {
                // snapshots: the overlay sits on a plain picture (an HwndHost can't be drawn into an image)
                VideoArea.Children.Remove(Video);
                SnapshotPicture.Visibility = Visibility.Visible;
            }
            else
            {
                Middle.Children.Remove(Overlay);
                Video.Content = Overlay;
                Video.Loaded += (s, e) => Video.MediaPlayer = Player.Mp;
            }

            Playlist.Loop = (LoopMode)settings.Loop;
            Playlist.Shuffle = settings.Shuffle;
            PlaylistList.ItemsSource = Playlist.Items;
            Playlist.Items.CollectionChanged += (s, e) => UpdatePlaylistCount();

            Vol.Value = settings.Volume;
            Player.Volume = settings.Volume;
            Player.Muted = settings.Muted;
            UpdateVolumeUi();
            ShowPlaylistPanel(settings.ShowPlaylist);
            UpdateLoopUi();
            ApplyUiScale();

            tick = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(250) };
            tick.Tick += (s, e) => Tick();
            hideTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.5) };
            hideTimer.Tick += (s, e) => { hideTimer.Stop(); HideFullscreenControls(); };
            osdTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.6) };
            osdTimer.Tick += (s, e) => { osdTimer.Stop(); Osd.Visibility = Visibility.Collapsed; };
            saveTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
            saveTimer.Tick += (s, e) => { saveTimer.Stop(); SaveSettings(); };
            if (!quiet) tick.Start();

            WireControls();
            WireMenus();
            WireKeys();
            RefreshRecent();
            RestorePlacement();
            UpdateDefaultPrompt();
            if (!string.IsNullOrEmpty(settings.LoadMessage)) ShowNotice(settings.LoadMessage);
            Closing += OnClosing;
            Closed += (s, e) => Player.Dispose();
            StateChanged += (s, e) => { if (WindowState == WindowState.Minimized && Settings.PauseOnMinimize && Player.IsPlaying) Player.Pause(); };
        }

        // ------------------------------------------------------------ the engine's events
        void HookPlayer()
        {
            Player.Playing += () =>
            {
                BtnPlay.Content = ""; BtnPlay.ToolTip = "Pause (Space)";
                BufferBox.Visibility = Visibility.Collapsed;
                UpdateOnTop();
                UpdateTaskbar();
                ApplyPictureSettings();
            };
            Player.Paused += () => { BtnPlay.Content = ""; BtnPlay.ToolTip = "Play (Space)"; UpdateOnTop(); UpdateTaskbar(); };
            Player.Stopped += () => { BtnPlay.Content = ""; BtnPlay.ToolTip = "Play (Space)"; UpdateOnTop(); UpdateTaskbar(); };
            Player.EndReached += () => Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(OnEnded));
            Player.Failed += m => { ShowNotice(m); Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => { if (Playlist.Count > 1) PlayNext(false, true); })); };
            Player.TimeChanged += t => { lastTime = t; if (!seeking) UpdateTimeUi(t); CheckAb(t); };
            Player.LengthChanged += l => { if (l > 0) { length = l; LblLength.Text = MediaFiles.Time(l); var c = Playlist.Current; if (c != null) { c.LengthMs = l; c.Duration = MediaFiles.Time(l); } } };
            Player.Buffering += c =>
            {
                if (c >= 100 || !MediaFiles.IsUrl(Player.CurrentPath ?? "")) { BufferBox.Visibility = Visibility.Collapsed; return; }
                BufferText.Text = "Loading... " + (int)c + "%";
                BufferBox.Visibility = Visibility.Visible;
            };
            Player.VideoChanged += UpdateAudioPanel;
            Player.TracksChanged += UpdateAudioPanel;
            Player.MetaChanged += UpdateMeta;
            Player.Rebuilt += () => { if (!Quiet) Video.MediaPlayer = Player.Mp; };
        }

        // ------------------------------------------------------------ opening things
        /// <summary>Adds files, folders, playlists and addresses; play = start the first one added.</summary>
        public void OpenPaths(IEnumerable<string> paths, bool play = true, bool next = false)
        {
            // one song or video opened to play (double-click in Explorer, Open, Recent, a drop): the others in its folder
            // come with it, so Next and Previous go through the folder, as in VLC
            var given = paths.ToList();
            if (play && !next && Settings.FolderNeighbours && given.Count == 1)
            {
                string one = given[0]?.Trim().Trim('"');
                if (!string.IsNullOrEmpty(one) && !MediaFiles.IsUrl(one) && MediaFiles.IsMedia(one) && File.Exists(one))
                {
                    var around = MediaFiles.Neighbours(one);
                    if (around.Count > 1)
                    {
                        Playlist.Clear();
                        Playlist.Add(around);
                        var chosen = Playlist.Items.FirstOrDefault(i => i.Path.Equals(Path.GetFullPath(one), StringComparison.OrdinalIgnoreCase)) ?? Playlist.Items[0];
                        PlayItem(chosen);
                        return;
                    }
                }
            }
            var list = ExpandAll(given);
            if (list.Count == 0) { ShowNotice("There was nothing Media Player can play there."); return; }
            // a lone subtitle file dropped on a playing video is added to it
            if (list.All(p => !MediaFiles.IsUrl(p) && MediaFiles.IsSubtitle(p)))
            {
                if (Player.HasMedia) { foreach (var s in list) Player.AddSubtitle(s); ShowOsd("Subtitles added"); }
                else ShowNotice("Open a video first, then add its subtitles.");
                return;
            }
            var titles = pendingTitles;
            pendingTitles = null;
            var first = Playlist.Add(list, next);
            if (titles != null)
                foreach (var it in Playlist.Items) if (titles.TryGetValue(it.Path, out var t) && !string.IsNullOrEmpty(t)) it.Title = t;
            if (play && first != null) PlayItem(first);
            else if (!Player.HasMedia && first != null) Playlist.Select(Playlist.Items.IndexOf(first));
        }
        Dictionary<string, string> pendingTitles;

        List<string> ExpandAll(IEnumerable<string> paths)
        {
            var result = new List<string>();
            foreach (string raw in paths)
            {
                if (string.IsNullOrWhiteSpace(raw)) continue;
                string p = raw.Trim().Trim('"');
                if (MediaFiles.IsUrl(p)) { result.Add(p); continue; }
                try
                {
                    if (Directory.Exists(p))
                    {
                        // a disc's folder: a DVD plays its main title set's .VOB files (this libVLC has no DVD
                        // menus), a Blu-ray folder plays as a Blu-ray
                        string videoTs = Discs.VideoTsFolder(p);
                        var main = videoTs != null ? Discs.MainTitleSet(videoTs) : null;
                        if (main != null && main.Files.Count > 0) { result.AddRange(main.Files); continue; }
                        if (Directory.Exists(Path.Combine(p, "BDMV"))) { result.Add(Discs.Address(DiscKind.BluRay, p)); continue; }
                        result.AddRange(MediaFiles.ExpandFolder(p));
                        Settings.LastFolder = p;
                        continue;
                    }
                    if (Playlist.IsPlaylistFile(p) && File.Exists(p))
                    {
                        pendingTitles ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                        foreach (var e in Core.Playlist.ReadFile(p)) { result.Add(e.Path); if (e.Title != null) pendingTitles[e.Path] = e.Title; }
                        continue;
                    }
                    if (File.Exists(p)) { result.Add(Path.GetFullPath(p)); continue; }
                }
                catch (Exception) { }
                result.Add(p);   // let libVLC say what's wrong
            }
            return result;
        }

        /// <summary>Plays a playlist item from where it was left (when that's on).</summary>
        public void PlayItem(PlaylistItem it)
        {
            int i = Playlist.Items.IndexOf(it);
            if (i < 0) return;
            RememberPosition();
            Playlist.Select(i);
            if (it.IsUrl && Online.WebVideos.IsWebOnly(it.Path)) { Online.WebVideos.OpenInEdge(it.Path, this); return; }
            long start = Settings.Resume && !it.IsUrl ? Settings.ResumeAt(it.Path) : 0;
            length = -1; lastTime = 0; abA = abB = -1; AbMark.Visibility = Visibility.Collapsed;
            LblTime.Text = MediaFiles.Time(0); LblLength.Text = "--:--"; Seek.Value = 0;
            Player.Open(it.Path, start, MediaOptionsFor(it.Path));
            Welcome.Visibility = Visibility.Collapsed;
            Title = it.Title + " - Media Player";
            LblSong.Text = it.Title; LblArtist.Text = ""; ImgCover.Source = null;
            Settings.AddRecent(it.Path);
            if (!it.IsUrl) Settings.LastFolder = Path.GetDirectoryName(it.Path) ?? Settings.LastFolder;
            if (start > 0) ShowOsd("Carrying on from " + MediaFiles.Time(start) + "  (Home starts over)", 3);
            else ShowOsd(it.Title);
            RefreshRecent();
            SaveSoon();
            PlaylistList.ScrollIntoView(it);
        }

        /// <summary>Options only this media gets: rotate / flip (libVLC's transform filter needs it at open).</summary>
        IEnumerable<string> MediaOptionsFor(string path)
        {
            if (rotate == 0) yield break;
            yield return ":video-filter=transform";
            yield return ":transform-type=" + rotate switch { 1 => "90", 2 => "180", 3 => "270", 4 => "hflip", _ => "vflip" };
        }

        void OnEnded()
        {
            var cur = Playlist.Current;
            if (cur != null) Settings.Positions.Remove(cur.Path);
            PlayNext(false, false);
        }

        public void PlayNext(bool userAsked, bool afterError = false)
        {
            if (Playlist.Count == 0) return;
            var n = Playlist.Next(userAsked);
            if (n == null)
            {
                if (!userAsked) { Player.Stop(); UpdateTimeUi(0); ShowOsd("End of the playlist"); }
                return;
            }
            if (afterError && n == Playlist.Current && Playlist.Loop == LoopMode.One) return;
            PlayItem(n);
        }

        public void PlayPrevious()
        {
            // like most players: after 5 seconds, Previous starts this one again
            if (Player.HasMedia && Player.Time > 5000) { Player.Time = 0; return; }
            var p = Playlist.Previous();
            if (p != null) PlayItem(p);
        }

        public void TogglePlay()
        {
            if (!Player.HasMedia)
            {
                if (Playlist.Count > 0) PlayItem(Playlist.Current ?? Playlist.Items[0]);
                else OpenFileDialog();
                return;
            }
            Player.TogglePause();
        }

        public void StopPlaying()
        {
            RememberPosition();
            Player.Stop();
            UpdateTimeUi(0);
            Seek.Value = 0;
            ShowOsd("Stopped");
        }

        void RememberPosition()
        {
            var cur = Playlist.Current;
            if (cur == null || cur.IsUrl || !Player.HasMedia) return;
            long t = Player.Time, l = Player.Length;
            if (t > 0 && l > 0) Settings.Remember(cur.Path, t, l);
        }

        // ------------------------------------------------------------ the regular update
        void Tick()
        {
            if (!Player.HasMedia) return;
            if (length <= 0) { long l = Player.Length; if (l > 0) { length = l; LblLength.Text = MediaFiles.Time(l); } }
            if (!seeking) UpdateTimeUi(Player.Time);
            Seek.IsEnabled = Player.Seekable;
        }

        void UpdateTimeUi(long t)
        {
            if (t < 0) t = 0;
            LblTime.Text = MediaFiles.Time(t);
            if (length > 0)
            {
                Seek.Value = Math.Clamp(t * 1000.0 / length, 0, 1000);
                LblLength.Text = showRemaining ? "-" + MediaFiles.Time(Math.Max(0, length - t)) : MediaFiles.Time(length);
                if (TaskbarItemInfo != null) TaskbarItemInfo.ProgressValue = t / (double)length;
            }
        }

        // ------------------------------------------------------------ controls
        void WireControls()
        {
            BtnPlay.Click += (s, e) => TogglePlay();
            BtnStop.Click += (s, e) => StopPlaying();
            BtnNext.Click += (s, e) => PlayNext(true);
            BtnPrev.Click += (s, e) => PlayPrevious();
            BtnFull.Click += (s, e) => ToggleFullscreen();
            BtnPlaylist.Click += (s, e) => ShowPlaylistPanel(BtnPlaylist.IsChecked == true);
            BtnLoop.Click += (s, e) => CycleLoop();
            BtnShuffle.Click += (s, e) => SetShuffle(BtnShuffle.IsChecked == true);
            BtnEffects.Click += (s, e) => ShowEffects(0);
            BtnSpeed.Click += (s, e) => OpenMenuUnder(BtnSpeed, BuildSpeedItems());
            BtnSubs.Click += (s, e) => OpenMenuUnder(BtnSubs, BuildTrackItems());
            BtnMute.Click += (s, e) => ToggleMute();
            BtnNoticeClose.Click += (s, e) => Notice.Visibility = Visibility.Collapsed;
            BtnWOpen.Click += (s, e) => OpenFileDialog();
            BtnWFolder.Click += (s, e) => OpenFolderDialog();
            BtnWDisc.Click += (s, e) => ShowOpenDisc();
            BtnWUrl.Click += (s, e) => ShowOpenUrl();
            BtnWOnline.Click += (s, e) => ShowOnline(null, null);
            BtnDefaultYes.Click += (s, e) => ShowMakeDefault();
            BtnDefaultNo.Click += (s, e) => { Settings.DefaultAsked = true; SaveSoon(); UpdateDefaultPrompt(); };
            LblLength.MouseLeftButtonUp += (s, e) => { showRemaining = !showRemaining; UpdateTimeUi(lastTime); LblLength.ToolTip = showRemaining ? "Click to show the length" : "Click to show the time left"; };

            Vol.ValueChanged += (s, e) =>
            {
                Player.Volume = (int)Math.Round(Vol.Value);
                Settings.Volume = Player.Volume;
                if (Player.Muted && e.OldValue != e.NewValue && Mouse.LeftButton == MouseButtonState.Pressed) Player.Muted = false;
                UpdateVolumeUi();
                SaveSoon();
            };
            Vol.MouseWheel += (s, e) => { ChangeVolume(e.Delta > 0 ? 5 : -5); e.Handled = true; };

            // the seek bar: drag to scrub (the picture follows), a tip shows the time under the pointer
            Seek.AddHandler(Thumb.DragStartedEvent, new DragStartedEventHandler((s, e) => seeking = true));
            Seek.AddHandler(Thumb.DragCompletedEvent, new DragCompletedEventHandler((s, e) => { seeking = false; SeekToSlider(); }));
            Seek.PreviewMouseLeftButtonUp += (s, e) => { if (!seeking) Dispatcher.BeginInvoke(new Action(SeekToSlider)); };
            Seek.ValueChanged += (s, e) =>
            {
                if (seeking && length > 0)
                {
                    long t = (long)(Seek.Value / 1000.0 * length);
                    LblTime.Text = MediaFiles.Time(t);
                    if (Math.Abs(t - lastScrub) > 400) { lastScrub = t; Player.Time = t; }
                }
            };
            Seek.MouseMove += (s, e) =>
            {
                if (length <= 0) { SeekTip.IsOpen = false; return; }
                double x = e.GetPosition(Seek).X;
                double f = Math.Clamp((x - 7) / Math.Max(1, Seek.ActualWidth - 14), 0, 1);
                SeekTipText.Text = MediaFiles.Time((long)(f * length));
                SeekTip.HorizontalOffset = x - 22; SeekTip.VerticalOffset = -30;
                SeekTip.IsOpen = true;
            };
            Seek.MouseLeave += (s, e) => SeekTip.IsOpen = false;
            Seek.MouseWheel += (s, e) => { Player.SeekBy(e.Delta > 0 ? 5000 : -5000); e.Handled = true; };

            // the playlist
            PlaylistList.MouseDoubleClick += (s, e) => { if (PlaylistList.SelectedItem is PlaylistItem it) PlayItem(it); };
            PlaylistList.KeyDown += (s, e) =>
            {
                if (e.Key == Key.Enter && PlaylistList.SelectedItem is PlaylistItem it) { PlayItem(it); e.Handled = true; }
                else if (e.Key == Key.Delete) { RemoveSelected(); e.Handled = true; }
            };
            PlaylistList.ContextMenuOpening += (s, e) => { PlaylistList.ContextMenu = BuildPlaylistMenu(); };
            PlaylistList.ContextMenu = new ContextMenu();
            BtnPlAdd.Click += (s, e) => OpenFileDialog(false);
            BtnPlMore.Click += (s, e) => { var m = BuildPlaylistMenu(); m.PlacementTarget = BtnPlMore; m.Placement = PlacementMode.Bottom; m.IsOpen = true; };
            TxtFilter.TextChanged += (s, e) => FilterPlaylist();
            SetupPlaylistDrag();

            // the picture: double-click = full screen, wheel = volume, right-click = everything
            Overlay.MouseLeftButtonDown += (s, e) => { if (e.ClickCount == 2 && Player.HasMedia) { ToggleFullscreen(); e.Handled = true; } };
            Overlay.MouseWheel += (s, e) => { if (Welcome.Visibility != Visibility.Visible) { ChangeVolume(e.Delta > 0 ? 5 : -5); e.Handled = true; } };
            Overlay.MouseMove += (s, e) => PointerMoved();
            Overlay.MouseRightButtonUp += (s, e) => { if (Welcome.Visibility == Visibility.Visible) return; var m = BuildContextMenu(); m.IsOpen = true; e.Handled = true; };
            Overlay.AllowDrop = true;
            Overlay.Drop += OnDrop;
            Overlay.DragOver += OnDragOver;
            Drop += OnDrop;
            DragOver += OnDragOver;
            MouseMove += (s, e) => PointerMoved();
        }
        long lastScrub = -1;

        void SeekToSlider()
        {
            if (length <= 0 || !Player.Seekable) return;
            long t = (long)(Seek.Value / 1000.0 * length);
            Player.Time = t;
            lastTime = t;
            LblTime.Text = MediaFiles.Time(t);
        }

        public void ChangeVolume(int delta)
        {
            Vol.Value = Math.Clamp(Vol.Value + delta, 0, 200);
            if (Player.Muted && delta > 0) { Player.Muted = false; Settings.Muted = false; UpdateVolumeUi(); }
            ShowOsd("Volume " + (int)Vol.Value + "%");
        }

        public void ToggleMute()
        {
            Player.Muted = !Player.Muted;
            Settings.Muted = Player.Muted;
            UpdateVolumeUi();
            ShowOsd(Player.Muted ? "Muted" : "Volume " + Player.Volume + "%");
            SaveSoon();
        }

        void UpdateVolumeUi()
        {
            int v = Player.Volume;
            LblVol.Text = v + "%";
            LblVol.Foreground = v > 100 ? (Brush)FindResource("Warn") : (Brush)FindResource("TextDim");
            BtnMute.Content = Player.Muted || v == 0 ? "" : v < 34 ? "" : v < 67 ? "" : "";
            BtnMute.ToolTip = Player.Muted ? "Unmute (M)" : "Mute (M)";
            Vol.Opacity = Player.Muted ? 0.4 : 1;
        }

        public void CycleLoop()
        {
            Playlist.Loop = (LoopMode)(((int)Playlist.Loop + 1) % 3);
            Settings.Loop = (int)Playlist.Loop;
            UpdateLoopUi();
            ShowOsd(Playlist.Loop switch { LoopMode.All => "Loop: the whole playlist", LoopMode.One => "Loop: this one", _ => "Loop: off" });
            SaveSoon();
        }
        void UpdateLoopUi()
        {
            BtnLoop.IsChecked = Playlist.Loop != LoopMode.Off;
            BtnLoop.Content = Playlist.Loop == LoopMode.One ? "" : "";
            BtnLoop.ToolTip = Playlist.Loop switch { LoopMode.All => "Loop: the whole playlist (L)", LoopMode.One => "Loop: this one (L)", _ => "Loop: off (L)" };
            BtnShuffle.IsChecked = Playlist.Shuffle;
        }
        public void SetShuffle(bool on)
        {
            Playlist.Shuffle = on;
            Settings.Shuffle = on;
            UpdateLoopUi();
            ShowOsd(on ? "Shuffle on" : "Shuffle off");
            SaveSoon();
        }

        public void SetSpeed(float r)
        {
            Player.Rate = r;
            BtnSpeed.Content = r.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + "x";
            ShowOsd("Speed " + BtnSpeed.Content);
        }
        public void ChangeSpeed(int dir)
        {
            float[] steps = { 0.25f, 0.5f, 0.67f, 0.75f, 0.8f, 0.9f, 1f, 1.1f, 1.25f, 1.5f, 1.75f, 2f, 2.5f, 3f, 4f };
            float r = Player.Rate;
            int i = Array.FindIndex(steps, s => s >= r - 0.001f);
            if (i < 0) i = steps.Length - 1;
            if (dir > 0) i = steps[i] > r + 0.001f ? i : Math.Min(steps.Length - 1, i + 1);
            else i = Math.Max(0, i - 1);
            SetSpeed(steps[i]);
        }

        // ------------------------------------------------------------ A-B repeat
        public void CycleAb()
        {
            if (!Player.HasMedia) return;
            if (abA < 0) { abA = Player.Time; ShowOsd("A-B repeat: A set at " + MediaFiles.Time(abA) + ". Press again for B."); }
            else if (abB < 0 && Player.Time > abA + 300) { abB = Player.Time; ShowOsd("Repeating " + MediaFiles.Time(abA) + " to " + MediaFiles.Time(abB)); }
            else { abA = abB = -1; ShowOsd("A-B repeat off"); }
            UpdateAbMark();
        }
        void UpdateAbMark()
        {
            if (abA < 0 || length <= 0) { AbMark.Visibility = Visibility.Collapsed; return; }
            double w = Seek.ActualWidth - 14;
            double a = 7 + w * abA / length, b = abB > 0 ? 7 + w * abB / length : a + 3;
            AbMark.Margin = new Thickness(a, 0, 0, 0);
            AbMark.Width = Math.Max(3, b - a);
            AbMark.Visibility = Visibility.Visible;
        }
        void CheckAb(long t)
        {
            if (abA >= 0 && abB > abA && t >= abB) Player.Time = abA;
        }

        // ------------------------------------------------------------ what's showing over the picture
        void UpdateAudioPanel()
        {
            if (!Player.HasMedia) return;
            bool hasVideo = Player.HasVideo || Player.VideoTracks().Count > 0;
            bool showMusic = !hasVideo && string.IsNullOrEmpty(Player.Options.Visual);
            AudioPanel.Visibility = showMusic ? Visibility.Visible : Visibility.Collapsed;
            if (showMusic) UpdateMeta();
        }

        void UpdateMeta()
        {
            var cur = Playlist.Current;
            if (cur == null) return;
            string title = Player.Meta(MetadataType.Title), artist = Player.Meta(MetadataType.Artist), album = Player.Meta(MetadataType.Album);
            string nowPlaying = Player.Meta(MetadataType.NowPlaying);
            if (!string.IsNullOrWhiteSpace(nowPlaying) && cur.IsUrl) { artist = nowPlaying; }
            if (!string.IsNullOrWhiteSpace(title) && !title.Equals(Path.GetFileName(cur.Path), StringComparison.OrdinalIgnoreCase) && !title.Contains("://"))
            {
                string t = !string.IsNullOrWhiteSpace(Player.Meta(MetadataType.Artist)) && !cur.IsUrl ? Player.Meta(MetadataType.Artist) + " - " + title : title;
                if (cur.Title != t && !cur.IsUrl) cur.Title = t;
                LblSong.Text = title;
            }
            else LblSong.Text = cur.Title;
            LblArtist.Text = string.Join("  ·  ", new[] { artist, album }.Where(x => !string.IsNullOrWhiteSpace(x)));
            string art = Player.Meta(MetadataType.ArtworkURL);
            if (!string.IsNullOrEmpty(art) && art.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    var bi = new BitmapImage();
                    bi.BeginInit(); bi.CacheOption = BitmapCacheOption.OnLoad; bi.UriSource = new Uri(art); bi.DecodePixelWidth = 480; bi.EndInit();
                    ImgCover.Source = bi;
                }
                catch (Exception) { ImgCover.Source = null; }
            }
            Title = cur.Title + " - Media Player";
        }

        public void ShowOsd(string text, double seconds = 1.6)
        {
            OsdText.Text = text;
            Osd.Visibility = Visibility.Visible;
            osdTimer.Stop();
            osdTimer.Interval = TimeSpan.FromSeconds(seconds);
            osdTimer.Start();
        }

        public void ShowNotice(string text)
        {
            NoticeText.Text = text;
            Notice.Visibility = Visibility.Visible;
        }

        public void RefreshRecent()
        {
            var list = new List<RecentEntry>();
            foreach (var r in Settings.Recent.Take(8))
            {
                long at = Settings.ResumeAt(r);
                list.Add(new RecentEntry
                {
                    Path = r,
                    Name = MediaFiles.DisplayName(r),
                    Glyph = MediaFiles.IsUrl(r) ? "" : MediaFiles.IsAudio(r) ? "" : "",
                    Note = at > 0 ? "carry on at " + MediaFiles.Time(at) : ""
                });
            }
            RecentList.ItemsSource = list;
            LblRecent.Visibility = list.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        void Recent_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.Tag is string p) OpenPaths(new[] { p });
        }

        // ------------------------------------------------------------ playlist panel
        public void ShowPlaylistPanel(bool show)
        {
            PlaylistCol.Width = show ? new GridLength(Math.Max(240, playlistWidth)) : new GridLength(0);
            PlaylistPanel.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            PlaylistSplitter.Visibility = PlaylistPanel.Visibility;
            BtnPlaylist.IsChecked = show;
            MnuPlaylist.IsChecked = show;
            if (Settings.ShowPlaylist != show) { Settings.ShowPlaylist = show; SaveSoon(); }
            UpdatePlaylistCount();
        }
        double playlistWidth = 300;

        void UpdatePlaylistCount()
        {
            long total = Playlist.Items.Sum(i => Math.Max(0, i.LengthMs));
            LblPlCount.Text = Playlist.Count == 0 ? "Drop files here to add them" :
                Playlist.Count + (Playlist.Count == 1 ? " item" : " items") + (total > 0 ? "  ·  " + MediaFiles.Time(total) : "");
        }

        void FilterPlaylist()
        {
            string f = TxtFilter.Text.Trim();
            var view = System.Windows.Data.CollectionViewSource.GetDefaultView(Playlist.Items);
            view.Filter = f.Length == 0 ? null : o => o is PlaylistItem it && (it.Title.Contains(f, StringComparison.OrdinalIgnoreCase) || it.Path.Contains(f, StringComparison.OrdinalIgnoreCase));
        }

        public void RemoveSelected()
        {
            var sel = PlaylistList.SelectedItems.Cast<PlaylistItem>().ToList();
            bool current = sel.Contains(Playlist.Current);
            foreach (var it in sel) Playlist.Remove(it);
            if (current) { Player.Stop(); ShowOsd("Removed"); }
        }

        // dragging items within the playlist to reorder them
        Point dragStart;
        PlaylistItem dragItem;
        void SetupPlaylistDrag()
        {
            PlaylistList.PreviewMouseLeftButtonDown += (s, e) =>
            {
                dragStart = e.GetPosition(PlaylistList);
                dragItem = (e.OriginalSource as FrameworkElement)?.DataContext as PlaylistItem;
            };
            PlaylistList.PreviewMouseMove += (s, e) =>
            {
                if (e.LeftButton != MouseButtonState.Pressed || dragItem == null) return;
                var d = e.GetPosition(PlaylistList) - dragStart;
                if (Math.Abs(d.Y) < 6 && Math.Abs(d.X) < 6) return;
                var it = dragItem; dragItem = null;
                DragDrop.DoDragDrop(PlaylistList, new DataObject("MediaPlayerSE.Item", it), DragDropEffects.Move);
            };
            PlaylistList.Drop += (s, e) =>
            {
                if (e.Data.GetDataPresent("MediaPlayerSE.Item"))
                {
                    var it = (PlaylistItem)e.Data.GetData("MediaPlayerSE.Item");
                    var target = (e.OriginalSource as FrameworkElement)?.DataContext as PlaylistItem;
                    int from = Playlist.Items.IndexOf(it), to = target != null ? Playlist.Items.IndexOf(target) : Playlist.Count - 1;
                    Playlist.Move(from, to);
                    e.Handled = true;
                    return;
                }
                if (e.Data.GetDataPresent(DataFormats.FileDrop))
                {
                    OpenPaths((string[])e.Data.GetData(DataFormats.FileDrop), !Player.HasMedia);
                    e.Handled = true;
                }
            };
        }

        // ------------------------------------------------------------ drag and drop from Explorer and browsers
        void OnDragOver(object sender, DragEventArgs e)
        {
            e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) || e.Data.GetDataPresent(DataFormats.UnicodeText) ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        }
        void OnDrop(object sender, DragEventArgs e)
        {
            if (e.Handled) return;
            if (e.Data.GetDataPresent(DataFormats.FileDrop)) { OpenPaths((string[])e.Data.GetData(DataFormats.FileDrop)); e.Handled = true; Activate(); return; }
            if (e.Data.GetDataPresent(DataFormats.UnicodeText))
            {
                string t = ((string)e.Data.GetData(DataFormats.UnicodeText) ?? "").Trim();
                if (MediaFiles.IsUrl(t)) { OpenPaths(new[] { t }); e.Handled = true; }
            }
        }

        // ------------------------------------------------------------ full screen and the minimal view
        public bool IsFullscreen => fullscreen;
        public void ToggleFullscreen() { SetFullscreen(!fullscreen); }
        public void SetFullscreen(bool on)
        {
            if (on == fullscreen) return;
            fullscreen = on;
            if (on)
            {
                stateBeforeFull = WindowState; styleBeforeFull = WindowStyle; resizeBeforeFull = ResizeMode;
                WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize;
                if (WindowState == WindowState.Maximized) WindowState = WindowState.Normal;   // so maximising again covers the taskbar
                WindowState = WindowState.Maximized;
                MenuBar.Visibility = Visibility.Collapsed;
                PlaylistCol.Width = new GridLength(0); PlaylistPanel.Visibility = Visibility.Collapsed; PlaylistSplitter.Visibility = Visibility.Collapsed;
                // the controls float over the picture
                Root.Children.Remove(Controls);
                Controls.VerticalAlignment = VerticalAlignment.Bottom;
                Controls.Background = new SolidColorBrush(Color.FromArgb(0xD8, 0x16, 0x16, 0x1A));
                Controls.Margin = new Thickness(40, 0, 40, 24);
                Controls.CornerRadius = new CornerRadius(10);
                Overlay.Children.Add(Controls);
                BtnFull.Content = ""; BtnFull.ToolTip = "Leave full screen (Esc)";
                PointerMoved();
            }
            else
            {
                Overlay.Children.Remove(Controls);
                Controls.ClearValue(VerticalAlignmentProperty); Controls.Background = (Brush)FindResource("Panel");
                Controls.Margin = new Thickness(0); Controls.CornerRadius = new CornerRadius(0); Controls.Visibility = Visibility.Visible; Controls.Opacity = 1;
                Root.Children.Add(Controls); Grid.SetRow(Controls, 2);
                WindowStyle = styleBeforeFull; ResizeMode = resizeBeforeFull;
                WindowState = stateBeforeFull;
                MenuBar.Visibility = minimal ? Visibility.Collapsed : Visibility.Visible;
                ShowPlaylistPanel(Settings.ShowPlaylist);
                BtnFull.Content = ""; BtnFull.ToolTip = "Full screen (F)";
                Overlay.Cursor = null;
                hideTimer.Stop();
            }
        }

        void PointerMoved()
        {
            if (!fullscreen) return;
            Controls.Visibility = Visibility.Visible;
            Overlay.Cursor = null;
            hideTimer.Stop();
            hideTimer.Start();
        }
        void HideFullscreenControls()
        {
            if (!fullscreen) return;
            if (Controls.IsMouseOver || !Player.IsPlaying) { hideTimer.Start(); return; }
            Controls.Visibility = Visibility.Collapsed;
            Overlay.Cursor = Cursors.None;
        }

        public void SetMinimal(bool on)
        {
            minimal = on;
            MnuMinimal.IsChecked = on;
            if (!fullscreen) MenuBar.Visibility = on ? Visibility.Collapsed : Visibility.Visible;
            if (on) ShowOsd("Minimal view: Ctrl+H brings the menu back", 3);
        }

        // ------------------------------------------------------------ picture settings that must be applied once playing
        void ApplyPictureSettings()
        {
            if (!string.IsNullOrEmpty(aspect)) Player.AspectRatio = aspect;
            if (!string.IsNullOrEmpty(crop)) Player.Crop = crop;
            if (zoom > 0) Player.Zoom = zoom;
            if (!string.IsNullOrEmpty(deinterlace)) Player.SetDeinterlace(deinterlace);
            ApplyAudioDevice();
            ApplyEqualizer();
            ReapplyAdjust?.Invoke();
        }
        /// <summary>Set by the effects window so picture adjustments survive the next file.</summary>
        public Action ReapplyAdjust;

        public void SetAspect(string a) { aspect = a ?? ""; Player.AspectRatio = aspect; ShowOsd("Aspect ratio: " + (aspect.Length == 0 ? "the video's own" : aspect)); }
        public void SetCrop(string c) { crop = c ?? ""; Player.Crop = crop; ShowOsd("Crop: " + (crop.Length == 0 ? "none" : crop)); }
        public void SetZoom(float z) { zoom = z; Player.Zoom = z; ShowOsd(z == 0 ? "Zoom: fit the window" : "Zoom: " + z.ToString("0.##") + "x"); }
        public void SetDeinterlace(string d) { deinterlace = d ?? ""; Player.SetDeinterlace(deinterlace); ShowOsd("Deinterlace: " + (deinterlace.Length == 0 ? "off" : deinterlace)); }
        public string Aspect => aspect;
        public string CropValue => crop;
        public float ZoomValue => zoom;
        public string DeinterlaceValue => deinterlace;
        public int Rotation => rotate;
        public void SetRotation(int r)
        {
            if (r == rotate) return;
            rotate = r;
            ShowOsd(r switch { 0 => "No rotation", 1 => "Turned 90°", 2 => "Turned 180°", 3 => "Turned 270°", 4 => "Flipped sideways", _ => "Flipped upside down" });
            var cur = Playlist.Current;
            if (cur != null && Player.HasMedia)
            {
                long t = Player.Time; bool paused = Player.IsPaused;
                Player.Open(cur.Path, t, MediaOptionsFor(cur.Path), paused);
            }
        }

        public void ApplyEqualizer()
        {
            var eq = Settings.Eq;
            Player.SetEqualizer(eq.On, eq.Preamp, eq.Bands);
        }
        public void ApplyAudioDevice()
        {
            if (!string.IsNullOrEmpty(Settings.AudioDevice)) Player.SetAudioDevice(Settings.AudioDevice);
        }

        /// <summary>Restarts the engine with new start-up options (subtitle size, levelling, visualisation, hardware decoding).</summary>
        public void RebuildEngine(Action<EngineOptions> change)
        {
            var o = Player.Options.Clone();
            change(o);
            Player.Rebuild(o, Playlist.Current != null ? MediaOptionsFor(Playlist.Current.Path) : null);
            Settings.SubtitleScale = o.SubtitleScale;
            Settings.Normalize = o.Normalize;
            Settings.Visual = o.Visual;
            Settings.HardwareDecoding = o.HardwareDecoding;
            SaveSoon();
        }

        // ------------------------------------------------------------ always on top, taskbar
        public void UpdateOnTop()
        {
            Topmost = Settings.OnTop == "always" || (Settings.OnTop == "playing" && Player.IsPlaying);
        }

        void UpdateTaskbar()
        {
            if (Quiet) return;
            TaskbarItemInfo ??= new System.Windows.Shell.TaskbarItemInfo();
            var ti = TaskbarItemInfo;
            if (ti.ThumbButtonInfos.Count == 0)
            {
                System.Windows.Shell.ThumbButtonInfo Make(string glyph, string tip, Action a)
                {
                    var b = new System.Windows.Shell.ThumbButtonInfo { Description = tip, ImageSource = GlyphImage(glyph) };
                    b.Click += (s, e) => a();
                    return b;
                }
                ti.ThumbButtonInfos.Add(Make("", "Previous", PlayPrevious));
                ti.ThumbButtonInfos.Add(Make("", "Play", TogglePlay));
                ti.ThumbButtonInfos.Add(Make("", "Next", () => PlayNext(true)));
            }
            ti.ThumbButtonInfos[1].ImageSource = GlyphImage(Player.IsPlaying ? "" : "");
            ti.ThumbButtonInfos[1].Description = Player.IsPlaying ? "Pause" : "Play";
            ti.ProgressState = Player.IsPlaying ? System.Windows.Shell.TaskbarItemProgressState.Normal :
                               Player.IsPaused ? System.Windows.Shell.TaskbarItemProgressState.Paused : System.Windows.Shell.TaskbarItemProgressState.None;
        }

        static ImageSource GlyphImage(string glyph)
        {
            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                var ft = new FormattedText(glyph, System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                    new Typeface(new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal), 16, Brushes.White, 1.0);
                dc.DrawText(ft, new Point((20 - ft.Width) / 2, (20 - ft.Height) / 2));
            }
            var bmp = new RenderTargetBitmap(20, 20, 96, 96, PixelFormats.Pbgra32);
            bmp.Render(dv);
            bmp.Freeze();
            return bmp;
        }

        // ------------------------------------------------------------ size and place, interface size
        void RestorePlacement()
        {
            Width = Settings.WinWidth; Height = Settings.WinHeight;
            if (!double.IsNaN(Settings.WinLeft) && !double.IsNaN(Settings.WinTop))
            {
                var r = new Rect(Settings.WinLeft, Settings.WinTop, Width, Height);
                var screen = new Rect(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop, SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);
                if (screen.IntersectsWith(new Rect(r.Left + 40, r.Top + 10, Math.Max(1, r.Width - 80), 40)))
                {
                    WindowStartupLocation = WindowStartupLocation.Manual;
                    Left = r.Left; Top = r.Top;
                }
                else WindowStartupLocation = WindowStartupLocation.CenterScreen;
            }
            else WindowStartupLocation = WindowStartupLocation.CenterScreen;
            if (Settings.WinMax) Loaded += (s, e) => WindowState = WindowState.Maximized;
        }

        void KeepPlacement()
        {
            if (fullscreen) return;
            Settings.WinMax = WindowState == WindowState.Maximized;
            var rb = RestoreBounds;
            if (!rb.IsEmpty && rb.Width > 0) { Settings.WinLeft = rb.Left; Settings.WinTop = rb.Top; Settings.WinWidth = rb.Width; Settings.WinHeight = rb.Height; }
        }

        public void ApplyUiScale()
        {
            double s = Settings.UiScale;
            Root.LayoutTransform = Math.Abs(s - 1) < 0.01 ? Transform.Identity : new ScaleTransform(s, s);
            Overlay.LayoutTransform = Root.LayoutTransform;
        }
        public void ChangeUiScale(double d)
        {
            Settings.UiScale = Math.Clamp(Math.Round((Settings.UiScale + d) * 20) / 20, 0.75, 2.0);
            ApplyUiScale();
            ShowOsd("Interface size " + (int)Math.Round(Settings.UiScale * 100) + "%");
            SaveSoon();
        }

        /// <summary>Fits the window to the video's own size (smaller if the screen is smaller).</summary>
        public void FitToVideo()
        {
            var (w, h) = Player.VideoSize();
            if (w == 0 || h == 0 || fullscreen) return;
            WindowState = WindowState.Normal;
            var src = PresentationSource.FromVisual(this);
            double dpi = src?.CompositionTarget?.TransformToDevice.M11 ?? 1;
            double vw = w / dpi, vh = h / dpi;
            double chromeW = ActualWidth - VideoArea.ActualWidth, chromeH = ActualHeight - VideoArea.ActualHeight;
            double maxW = SystemParameters.WorkArea.Width * 0.95, maxH = SystemParameters.WorkArea.Height * 0.95;
            double k = Math.Min(1, Math.Min((maxW - chromeW) / vw, (maxH - chromeH) / vh));
            Width = vw * k + chromeW; Height = vh * k + chromeH;
        }

        // ------------------------------------------------------------ saving
        public void SaveSoon() { if (!Quiet) { saveTimer.Stop(); saveTimer.Start(); } }
        public void SaveSettings()
        {
            if (Quiet) return;
            KeepPlacement();
            try { Settings.Save(); }
            catch (Exception ex) { ShowNotice("Your settings couldn't be saved: " + ex.Message); }
        }

        void OnClosing(object sender, CancelEventArgs e)
        {
            RememberPosition();
            saveTimer.Stop();
            SaveSettings();
            Online.WebVideos.CloseAll();
        }

        // ------------------------------------------------------------ file pickers
        public void OpenFileDialog(bool play = true)
        {
            var d = new Microsoft.Win32.OpenFileDialog
            {
                Title = play ? "Open media" : "Add to the playlist",
                Multiselect = true,
                Filter = "Media files|" + string.Join(";", MediaFiles.Video.Concat(MediaFiles.Audio).Concat(MediaFiles.Playlists).Select(x => "*" + x)) +
                         "|Videos|" + string.Join(";", MediaFiles.Video.Select(x => "*" + x)) +
                         "|Music and sound|" + string.Join(";", MediaFiles.Audio.Select(x => "*" + x)) +
                         "|Playlists|*.m3u;*.m3u8;*.pls|All files|*.*",
                InitialDirectory = Directory.Exists(Settings.LastFolder) ? Settings.LastFolder : Environment.GetFolderPath(Environment.SpecialFolder.MyVideos)
            };
            if (d.ShowDialog(this) == true) OpenPaths(d.FileNames, play || !Player.HasMedia);
        }

        public void OpenFolderDialog()
        {
            var d = new Microsoft.Win32.OpenFolderDialog
            {
                Title = "Play everything in a folder",
                InitialDirectory = Directory.Exists(Settings.LastFolder) ? Settings.LastFolder : Environment.GetFolderPath(Environment.SpecialFolder.MyVideos)
            };
            if (d.ShowDialog(this) == true) OpenPaths(new[] { d.FolderName });
        }

        public void AddSubtitleDialog()
        {
            if (!Player.HasMedia) { ShowNotice("Open a video first, then add its subtitles."); return; }
            var d = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Add subtitles",
                Filter = "Subtitles|" + string.Join(";", MediaFiles.Subtitles.Select(x => "*" + x)) + "|All files|*.*",
                InitialDirectory = Playlist.Current != null && !Playlist.Current.IsUrl ? Path.GetDirectoryName(Playlist.Current.Path) : Settings.LastFolder
            };
            if (d.ShowDialog(this) == true && Player.AddSubtitle(d.FileName)) ShowOsd("Subtitles: " + Path.GetFileName(d.FileName));
        }

        public void AddAudioDialog()
        {
            if (!Player.HasMedia) { ShowNotice("Open a video first, then add a sound file to it."); return; }
            var d = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Add a sound track",
                Filter = "Sound|" + string.Join(";", MediaFiles.Audio.Select(x => "*" + x)) + "|All files|*.*"
            };
            if (d.ShowDialog(this) == true && Player.AddAudio(d.FileName)) ShowOsd("Sound track added: " + Path.GetFileName(d.FileName));
        }

        public void SavePlaylistDialog()
        {
            if (Playlist.Count == 0) { ShowNotice("The playlist is empty."); return; }
            var d = new Microsoft.Win32.SaveFileDialog { Title = "Save the playlist", Filter = "Playlist (*.m3u8)|*.m3u8", FileName = "Playlist.m3u8", InitialDirectory = Settings.LastFolder };
            if (d.ShowDialog(this) != true) return;
            try { Playlist.Save(d.FileName); ShowOsd("Playlist saved"); }
            catch (Exception ex) { ShowNotice("The playlist couldn't be saved: " + ex.Message); }
        }

        public void TakeSnapshot()
        {
            if (!Player.HasMedia || !Player.HasVideo) { ShowOsd("There's no picture to save"); return; }
            string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Media Player");
            Directory.CreateDirectory(dir);
            string name = string.Concat((Playlist.Current?.Title ?? "Snapshot").Split(Path.GetInvalidFileNameChars()));
            if (name.Length > 60) name = name.Substring(0, 60);
            string file = Path.Combine(dir, $"{name} {MediaFiles.Time(Player.Time).Replace(':', '.')} {DateTime.Now:HHmmss}.png");
            if (Player.Snapshot(file)) ShowOsd("Snapshot saved in Pictures\\Media Player", 2.5);
            else ShowOsd("The snapshot couldn't be saved");
        }

        /// <summary>Things passed from a second start (Explorer's Open with, the command line).</summary>
        public void HandleArgs(string[] args, bool enqueue)
        {
            var files = args.Where(a => !a.StartsWith("--")).ToList();
            if (files.Count > 0) OpenPaths(files, !enqueue);
            if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
            Activate();
        }
    }
}
