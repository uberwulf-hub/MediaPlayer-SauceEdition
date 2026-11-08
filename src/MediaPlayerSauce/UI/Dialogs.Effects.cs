// The Effects and filters window (Tools > Effects and filters, Ctrl+E; Synchronisation opens its Sync tab): equalizer,
// sound, picture and sync. One window that stays open beside the player and changes things as you move them.
// FxKit holds the small building blocks this window, Media information and Convert share.
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using LibVLCSharp.Shared;
using MediaPlayerSauce.Core;
using MediaPlayerSauce.Engine;

namespace MediaPlayerSauce.UI
{
    internal static class FxKit
    {
        public static Style S(string key) { return (Style)Application.Current.FindResource(key); }
        public static Brush B(string key) { return (Brush)Application.Current.FindResource(key); }
        public static TextBlock H2(string text, double top = 14) { return new TextBlock { Text = text, Style = S("H2"), Margin = new Thickness(0, top, 0, 6) }; }
        public static TextBlock Dim(string text, double top = 0)
        {
            return new TextBlock { Text = text, Style = S("Dim"), FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, top, 0, 0) };
        }
        public static Button Btn(string text, Action click, bool accent = false)
        {
            var b = new Button { Content = text, MinWidth = 80, Margin = new Thickness(0, 0, 8, 0) };
            if (accent) b.Style = S("AccentBtn");
            if (click != null) b.Click += (s, e) => click();
            return b;
        }
        public static StackPanel Row(params UIElement[] items)
        {
            var p = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 4) };
            foreach (var i in items) p.Children.Add(i);
            return p;
        }
        /// <summary>A label column and a control column.</summary>
        public static Grid Form()
        {
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(170) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            return g;
        }
        public static void AddRow(Grid g, string label, UIElement control)
        {
            int r = g.RowDefinitions.Count;
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var l = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 5, 10, 5), TextWrapping = TextWrapping.Wrap };
            Grid.SetRow(l, r); g.Children.Add(l);
            if (control is FrameworkElement fe && fe.Margin == default) fe.Margin = new Thickness(0, 5, 0, 5);
            Grid.SetRow(control, r); Grid.SetColumn(control, 1); g.Children.Add(control);
        }
        public static ComboBox Combo(IEnumerable<string> items, int selected, Action<int> changed)
        {
            var c = new ComboBox { MinWidth = 220, HorizontalAlignment = HorizontalAlignment.Left };
            foreach (var i in items) c.Items.Add(i);
            c.SelectedIndex = Math.Clamp(selected, -1, c.Items.Count - 1);
            c.SelectionChanged += (s, e) => changed(c.SelectedIndex);
            return c;
        }
        public static string Num(double v, string f = "0.##") { return v.ToString(f, CultureInfo.InvariantCulture); }
        /// <summary>A row of TabPill radio buttons over pages; returns the host and a function that shows a page.</summary>
        public static (Grid Host, Action<int> Show) Tabs(string[] names, Func<int, UIElement> build)
        {
            var host = new Grid();
            host.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            host.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            var bar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(16, 14, 16, 4) };
            host.Children.Add(bar);
            var pages = new UIElement[names.Length];
            var pills = new RadioButton[names.Length];
            string group = "tabs" + Guid.NewGuid().ToString("N");
            var area = new Border { Margin = new Thickness(0, 4, 0, 0) };
            Grid.SetRow(area, 1);
            host.Children.Add(area);
            Action<int> show = i =>
            {
                i = Math.Clamp(i, 0, names.Length - 1);
                pages[i] ??= build(i);
                area.Child = pages[i];
                if (pills[i].IsChecked != true) pills[i].IsChecked = true;
            };
            for (int i = 0; i < names.Length; i++)
            {
                int k = i;
                pills[i] = new RadioButton { Content = names[i], Style = S("TabPill"), GroupName = group };
                pills[i].Checked += (s, e) => show(k);
                bar.Children.Add(pills[i]);
            }
            return (host, show);
        }
        public static ScrollViewer Page(UIElement content)
        {
            return new ScrollViewer { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Padding = new Thickness(20, 6, 20, 16) };
        }
    }

    public sealed class EffectsWindow : Window
    {
        readonly MainWindow main;
        readonly Action<int> show;
        readonly DispatcherTimer refresh;
        bool loading;

        public EffectsWindow(MainWindow main)
        {
            this.main = main;
            Style = FxKit.S("Dialog");
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Owner = main;
            Title = "Effects and filters";
            Width = 620; Height = 560; MinWidth = 480; MinHeight = 400;
            ResizeMode = ResizeMode.CanResizeWithGrip;
            var (host, s) = FxKit.Tabs(new[] { "Equalizer", "Sound", "Picture", "Sync" }, Build);
            show = s;
            var root = new DockPanel();
            var close = new Button { Content = "Close", MinWidth = 90, IsCancel = true, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(16, 6, 16, 14) };
            close.Click += (o, e) => Close();
            DockPanel.SetDock(close, Dock.Bottom);
            root.Children.Add(close);
            root.Children.Add(host);
            Content = root;
            refresh = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            refresh.Tick += (o, e) => RefreshSync();
            Loaded += (o, e) => refresh.Start();
            Closed += (o, e) => refresh.Stop();
        }

        public void ShowTab(int tab) { show(tab); }

        UIElement Build(int tab)
        {
            loading = true;
            try
            {
                return tab switch { 0 => BuildEq(), 1 => BuildSound(), 2 => BuildPicture(), _ => BuildSync() };
            }
            finally { loading = false; }
        }

        // ------------------------------------------------------------ equalizer
        ComboBox eqPreset;
        Slider eqPreamp;
        readonly Slider[] eqBands = new Slider[10];
        readonly TextBlock[] eqValues = new TextBlock[11];
        CheckBox eqOn;
        List<string> presets;

        UIElement BuildEq()
        {
            var eq = main.Settings.Eq;
            var p = new StackPanel();
            eqOn = new CheckBox { Content = "Use the equalizer", IsChecked = eq.On, Margin = new Thickness(0, 8, 0, 4) };
            eqOn.Click += (s, e) => { eq.On = eqOn.IsChecked == true; ApplyEq(); };
            p.Children.Add(eqOn);
            p.Children.Add(FxKit.Dim("Raise or lower the bass (left) and the treble (right). A ready-made setting is a good start."));
            presets = new List<string> { "Your own" };
            try { presets.AddRange(Player.EqPresets()); } catch (Exception) { }
            eqPreset = FxKit.Combo(presets, eq.Preset + 1, i => { if (!loading) ChoosePreset(i - 1); });
            var reset = FxKit.Btn("Reset", () => { eq.Preset = -1; eq.Preamp = 12f; eq.Bands = new float[10]; ShowEqValues(); ApplyEq(); });
            reset.Margin = new Thickness(8, 0, 0, 0);
            var top = FxKit.Row(new TextBlock { Text = "Setting", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) }, eqPreset, reset);
            top.Margin = new Thickness(0, 12, 0, 8);
            p.Children.Add(top);

            float[] freqs;
            try { freqs = Player.EqFrequencies(); } catch (Exception) { freqs = new float[] { 60, 170, 310, 600, 1000, 3000, 6000, 12000, 14000, 16000 }; }
            var bands = new Grid { Margin = new Thickness(0, 8, 0, 0), HorizontalAlignment = HorizontalAlignment.Left };
            bands.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            bands.RowDefinitions.Add(new RowDefinition { Height = new GridLength(200) });
            bands.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            for (int i = 0; i < 11; i++)
            {
                bands.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(i == 0 ? 64 : 46) });
                int k = i;
                var sl = new Slider { Style = FxKit.S("EqSlider"), Minimum = -20, Maximum = 20, SmallChange = 0.5, LargeChange = 2, HorizontalAlignment = HorizontalAlignment.Center };
                sl.ToolTip = i == 0 ? "Overall loudness before the bands" : FreqName(freqs[Math.Min(i - 1, freqs.Length - 1)]);
                sl.ValueChanged += (s, e) => { if (!loading) BandMoved(k, (float)Math.Round(e.NewValue * 2) / 2); };
                sl.MouseDoubleClick += (s, e) => { sl.Value = k == 0 ? 12 : 0; };
                var val = new TextBlock { Style = FxKit.S("Dim"), FontSize = 11, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 4) };
                var name = new TextBlock { Text = i == 0 ? "Overall" : FreqName(freqs[Math.Min(i - 1, freqs.Length - 1)]), FontSize = 11, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 6, 0, 0) };
                if (i == 0) { name.FontWeight = FontWeights.SemiBold; eqPreamp = sl; } else eqBands[i - 1] = sl;
                eqValues[i] = val;
                Grid.SetColumn(val, i); Grid.SetColumn(sl, i); Grid.SetColumn(name, i);
                Grid.SetRow(sl, 1); Grid.SetRow(name, 2);
                bands.Children.Add(val); bands.Children.Add(sl); bands.Children.Add(name);
            }
            p.Children.Add(bands);
            p.Children.Add(FxKit.Dim("Each slider goes from -20 dB (quieter) to +20 dB (louder). Double-click a slider to put it back in the middle.", 10));
            ShowEqValues();
            return FxKit.Page(p);
        }

        static string FreqName(float f)
        {
            return f >= 1000 ? FxKit.Num(f / 1000, "0.#") + " kHz" : FxKit.Num(f, "0") + " Hz";
        }

        void ShowEqValues()
        {
            var eq = main.Settings.Eq;
            bool was = loading; loading = true;
            try
            {
                eqPreamp.Value = eq.Preamp;
                for (int i = 0; i < 10; i++) eqBands[i].Value = i < eq.Bands.Length ? eq.Bands[i] : 0;
                eqPreset.SelectedIndex = Math.Clamp(eq.Preset + 1, 0, eqPreset.Items.Count - 1);
                eqOn.IsChecked = eq.On;
                UpdateEqLabels();
            }
            finally { loading = was; }
        }
        void UpdateEqLabels()
        {
            eqValues[0].Text = FxKit.Num(eqPreamp.Value, "+0.#;-0.#;0");
            for (int i = 0; i < 10; i++) eqValues[i + 1].Text = FxKit.Num(eqBands[i].Value, "+0.#;-0.#;0");
        }

        void ChoosePreset(int preset)
        {
            var eq = main.Settings.Eq;
            eq.Preset = preset;
            if (preset >= 0)
            {
                try
                {
                    var b = Player.EqPresetBands(preset, out float pre);
                    eq.Preamp = pre;
                    eq.Bands = new float[10];
                    Array.Copy(b, eq.Bands, Math.Min(10, b.Length));
                }
                catch (Exception) { }
                eq.On = true;
            }
            ShowEqValues();
            ApplyEq();
            if (preset >= 0) main.ShowOsd("Equalizer: " + presets[preset + 1]);
        }

        void BandMoved(int k, float v)
        {
            var eq = main.Settings.Eq;
            if (k == 0) eq.Preamp = v;
            else { if (eq.Bands.Length < 10) Array.Resize(ref eq.Bands, 10); eq.Bands[k - 1] = v; }
            eq.Preset = -1;
            eq.On = true;
            bool was = loading; loading = true;
            eqPreset.SelectedIndex = 0; eqOn.IsChecked = true;
            loading = was;
            UpdateEqLabels();
            ApplyEq();
        }

        void ApplyEq()
        {
            try { main.ApplyEqualizer(); } catch (Exception) { }
            main.SaveSoon();
        }

        // ------------------------------------------------------------ sound
        UIElement BuildSound()
        {
            var p = new StackPanel();
            var g = FxKit.Form();

            var devices = new List<string> { "Windows' default" };
            try { devices.AddRange(main.Player.AudioDevices().Select(d => d.Name)); } catch (Exception) { }
            string curDev = main.Settings.AudioDevice ?? "";
            if (curDev.Length > 0 && !devices.Contains(curDev)) devices.Add(curDev);
            var dev = FxKit.Combo(devices, curDev.Length == 0 ? 0 : devices.IndexOf(curDev), i =>
            {
                if (loading || i < 0) return;
                string name = i == 0 ? "" : devices[i];
                main.Settings.AudioDevice = name;
                try { main.Player.SetAudioDevice(name.Length == 0 ? null : name); } catch (Exception) { }
                main.SaveSoon();
                main.ShowOsd("Sound: " + devices[i]);
            });
            FxKit.AddRow(g, "Play the sound on", dev);

            (string Name, AudioOutputChannel Ch)[] modes = { ("Stereo", AudioOutputChannel.Stereo), ("Swap left and right", AudioOutputChannel.RStereo),
                ("Left only", AudioOutputChannel.Left), ("Right only", AudioOutputChannel.Right), ("Surround (Dolby)", AudioOutputChannel.Dolbys) };
            int curMode = 0;
            try { curMode = Math.Max(0, Array.FindIndex(modes, m => m.Ch == main.Player.Stereo)); } catch (Exception) { }
            var stereo = FxKit.Combo(modes.Select(m => m.Name), curMode, i =>
            {
                if (loading || i < 0) return;
                try { main.Player.Stereo = modes[i].Ch; main.ShowOsd("Sound: " + modes[i].Name); }
                catch (Exception) { main.ShowOsd("Play something first, then choose this"); }
            });
            FxKit.AddRow(g, "Left and right", stereo);
            p.Children.Add(g);

            var norm = new CheckBox { Content = "Even out loud and quiet parts", IsChecked = main.Player.Options.Normalize, Margin = new Thickness(0, 12, 0, 0) };
            norm.Click += (s, e) => { bool on = norm.IsChecked == true; main.RebuildEngine(o => o.Normalize = on); main.ShowOsd(on ? "Evening out the volume" : "Volume as recorded"); };
            p.Children.Add(norm);
            p.Children.Add(FxKit.Dim("Handy for films where talking is quiet and explosions are loud. Playback restarts for a moment when you change it.", 4));

            var visuals = new[] { ("Off", ""), ("Spectrum", "spectrum"), ("Wave", "scope"), ("Level meter", "vuMeter"), ("Spectrum and wave", "visual"), ("Swirls (Goom)", "goom") };
            var vg = FxKit.Form();
            vg.Margin = new Thickness(0, 14, 0, 0);
            var vis = FxKit.Combo(visuals.Select(v => v.Item1), Math.Max(0, Array.FindIndex(visuals, v => v.Item2 == main.Player.Options.Visual)), i =>
            {
                if (loading || i < 0) return;
                string v = visuals[i].Item2;
                main.RebuildEngine(o => o.Visual = v);
                main.ShowOsd("Pictures for music: " + visuals[i].Item1);
            });
            FxKit.AddRow(vg, "Pictures for music", vis);
            p.Children.Add(vg);
            p.Children.Add(FxKit.Dim("Moving pictures that follow the music while a song plays."));
            return FxKit.Page(p);
        }

        // ------------------------------------------------------------ picture
        readonly Slider[] adj = new Slider[5];
        readonly TextBlock[] adjVal = new TextBlock[5];
        CheckBox adjOn;
        static readonly (string Name, double Min, double Max, double Def)[] AdjInfo =
        {
            ("Brightness", 0, 2, 1), ("Contrast", 0, 2, 1), ("Colour", 0, 3, 1), ("Colour shift", -180, 180, 0), ("Gamma", 0.01, 10, 1),
        };

        UIElement BuildPicture()
        {
            var st = main.PictureAdjust;
            var p = new StackPanel();
            adjOn = new CheckBox { Content = "Adjust the picture", IsChecked = st.On, Margin = new Thickness(0, 8, 0, 8) };
            adjOn.Click += (s, e) => { st.On = adjOn.IsChecked == true; ApplyAdjust(); };
            p.Children.Add(adjOn);
            var g = FxKit.Form();
            for (int i = 0; i < 5; i++)
            {
                int k = i;
                var info = AdjInfo[i];
                var sl = new Slider { Style = FxKit.S("FormSlider"), Minimum = info.Min, Maximum = info.Max, Value = st.Values[i], MinWidth = 220,
                    SmallChange = (info.Max - info.Min) / 100, LargeChange = (info.Max - info.Min) / 10, VerticalAlignment = VerticalAlignment.Center };
                sl.ValueChanged += (s, e) =>
                {
                    if (loading) return;
                    st.Values[k] = (float)e.NewValue; st.On = true;
                    adjOn.IsChecked = true;
                    adjVal[k].Text = AdjText(k, e.NewValue);
                    ApplyAdjust();
                };
                sl.MouseDoubleClick += (s, e) => sl.Value = AdjInfo[k].Def;
                adj[i] = sl;
                adjVal[i] = new TextBlock { Text = AdjText(i, st.Values[i]), Width = 50, Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, Style = FxKit.S("Dim") };
                var row = new DockPanel();
                DockPanel.SetDock(adjVal[i], Dock.Right);
                row.Children.Add(adjVal[i]); row.Children.Add(sl);
                FxKit.AddRow(g, info.Name, row);
            }
            p.Children.Add(g);
            var reset = FxKit.Btn("Reset", () =>
            {
                st.On = false;
                for (int i = 0; i < 5; i++) st.Values[i] = (float)AdjInfo[i].Def;
                loading = true;
                for (int i = 0; i < 5; i++) { adj[i].Value = st.Values[i]; adjVal[i].Text = AdjText(i, st.Values[i]); }
                adjOn.IsChecked = false;
                loading = false;
                ApplyAdjust();
            });
            reset.HorizontalAlignment = HorizontalAlignment.Left;
            reset.Margin = new Thickness(0, 6, 0, 0);
            p.Children.Add(reset);
            p.Children.Add(FxKit.Dim("These stay for the next videos until you close Media Player.", 6));

            p.Children.Add(FxKit.H2("Shape and position", 18));
            var f = FxKit.Form();
            string[] rot = { "None", "Turn 90\u00B0 right", "Turn 180\u00B0", "Turn 90\u00B0 left", "Flip sideways", "Flip upside down" };
            FxKit.AddRow(f, "Turn or flip", FxKit.Combo(rot, main.Rotation, i => { if (!loading && i >= 0) main.SetRotation(i); }));
            var aspects = new[] { ("The video's own", ""), ("16:9", "16:9"), ("4:3", "4:3"), ("21:9 (cinema)", "21:9"), ("2.35:1", "235:100"), ("16:10", "16:10"), ("1:1 (square)", "1:1"), ("5:4", "5:4") };
            FxKit.AddRow(f, "Shape (aspect ratio)", FxKit.Combo(aspects.Select(a => a.Item1), Array.FindIndex(aspects, a => a.Item2 == main.Aspect), i => { if (!loading && i >= 0) main.SetAspect(aspects[i].Item2); }));
            var crops = new[] { ("None", ""), ("16:9", "16:9"), ("4:3", "4:3"), ("21:9", "21:9"), ("2.35:1", "235:100"), ("16:10", "16:10"), ("1:1", "1:1") };
            FxKit.AddRow(f, "Cut off the edges (crop)", FxKit.Combo(crops.Select(a => a.Item1), Array.FindIndex(crops, a => a.Item2 == main.CropValue), i => { if (!loading && i >= 0) main.SetCrop(crops[i].Item2); }));
            var zooms = new[] { ("Fit the window", 0f), ("Quarter size", 0.25f), ("Half size", 0.5f), ("Actual size", 1f), ("Double size", 2f) };
            FxKit.AddRow(f, "Size", FxKit.Combo(zooms.Select(a => a.Item1), Array.FindIndex(zooms, a => Math.Abs(a.Item2 - main.ZoomValue) < 0.01f), i => { if (!loading && i >= 0) main.SetZoom(zooms[i].Item2); }));
            var deint = new[] { ("Off", ""), ("Blend", "blend"), ("Bob", "bob"), ("Linear", "linear"), ("Mean", "mean"), ("Yadif (best)", "yadif"), ("Yadif 2x (smoothest)", "yadif2x"), ("Discard", "discard"), ("X", "x") };
            FxKit.AddRow(f, "Smooth out stripes (deinterlace)", FxKit.Combo(deint.Select(a => a.Item1), Array.FindIndex(deint, a => a.Item2 == main.DeinterlaceValue), i => { if (!loading && i >= 0) main.SetDeinterlace(deint[i].Item2); }));
            p.Children.Add(f);
            p.Children.Add(FxKit.Dim("Stripes on moving things happen with old TV recordings and DVDs; deinterlacing smooths them out.", 4));
            return FxKit.Page(p);
        }

        static string AdjText(int i, double v)
        {
            return i == 3 ? FxKit.Num(v, "0") + "\u00B0" : FxKit.Num(v, "0.00");
        }

        void ApplyAdjust()
        {
            main.ReapplyAdjust ??= main.ApplyPictureAdjust;
            main.ApplyPictureAdjust();
        }

        // ------------------------------------------------------------ sync
        TextBox audioDelay, subDelay;
        Slider speed;
        TextBlock speedText;

        UIElement BuildSync()
        {
            var p = new StackPanel();
            p.Children.Add(FxKit.H2("Sound and picture out of step?", 8));
            p.Children.Add(FxKit.Dim("Positive numbers make the sound or the subtitles come later, negative numbers earlier."));
            var g = FxKit.Form();
            g.Margin = new Thickness(0, 8, 0, 0);
            audioDelay = new TextBox { Width = 80, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
            FxKit.AddRow(g, "Sound delay", DelayRow(audioDelay, 50, () => main.Player.AudioDelayMs, v => main.Player.AudioDelayMs = v, "Sound "));
            subDelay = new TextBox { Width = 80, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
            FxKit.AddRow(g, "Subtitle delay", DelayRow(subDelay, 100, () => main.Player.SubtitleDelayMs, v => main.Player.SubtitleDelayMs = v, "Subtitles "));
            p.Children.Add(g);
            p.Children.Add(FxKit.Dim("The sound moves in steps of 0.05 seconds and subtitles in steps of 0.1 seconds. You can also type a number.", 2));

            p.Children.Add(FxKit.H2("Subtitles", 18));
            var sg = FxKit.Form();
            var sizes = new[] { ("Small", 75), ("Normal", 100), ("Large", 130), ("Larger", 160), ("Huge", 200) };
            int cur = main.Player.Options.SubtitleScale;
            int sel = Array.FindIndex(sizes, x => x.Item2 == cur);
            var sizeBox = FxKit.Combo(sizes.Select(x => x.Item1), sel, i =>
            {
                if (loading || i < 0) return;
                int v = sizes[i].Item2;
                main.RebuildEngine(o => o.SubtitleScale = v);
                main.ShowOsd("Subtitle size: " + sizes[i].Item1);
            });
            FxKit.AddRow(sg, "Subtitle size", sizeBox);
            p.Children.Add(sg);

            p.Children.Add(FxKit.H2("Playback speed", 18));
            // the slider is in powers of two (-2 = 0.25x, 0 = normal, 2 = 4x), so slower and faster get the same room
            speed = new Slider { Style = FxKit.S("FormSlider"), Minimum = -2, Maximum = 2, SmallChange = 0.05, LargeChange = 0.25, MinWidth = 260, VerticalAlignment = VerticalAlignment.Center };
            speed.Value = Math.Log2(Math.Clamp(SafeRate(), 0.25, 4));
            speedText = new TextBlock { Width = 60, Margin = new Thickness(10, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center, Text = RateText(SafeRate()) };
            speed.ValueChanged += (s, e) =>
            {
                if (loading) return;
                double r = Math.Pow(2, e.NewValue);
                r = Math.Abs(r - 1) < 0.03 ? 1 : Math.Round(r, 2);
                speedText.Text = RateText(r);
                main.SetSpeed((float)r);
            };
            var normal = FxKit.Btn("Normal", () => { speed.Value = 0; main.SetSpeed(1f); speedText.Text = RateText(1); });
            var sp = new DockPanel();
            DockPanel.SetDock(normal, Dock.Right); DockPanel.SetDock(speedText, Dock.Right);
            sp.Children.Add(normal); sp.Children.Add(speedText); sp.Children.Add(speed);
            p.Children.Add(sp);
            var ends = new Grid { Margin = new Thickness(0, 0, 150, 0) };
            ends.Children.Add(new TextBlock { Text = "Slower (0.25x)", Style = FxKit.S("Dim"), FontSize = 11, HorizontalAlignment = HorizontalAlignment.Left });
            ends.Children.Add(new TextBlock { Text = "Normal", Style = FxKit.S("Dim"), FontSize = 11, HorizontalAlignment = HorizontalAlignment.Center });
            ends.Children.Add(new TextBlock { Text = "Faster (4x)", Style = FxKit.S("Dim"), FontSize = 11, HorizontalAlignment = HorizontalAlignment.Right });
            p.Children.Add(ends);
            RefreshSync();
            return FxKit.Page(p);
        }

        double SafeRate() { try { return main.Player.Rate; } catch (Exception) { return 1; } }
        static string RateText(double r) { return FxKit.Num(r, "0.##") + "x"; }
        static string Secs(long ms) { return (ms / 1000.0).ToString("0.00", CultureInfo.InvariantCulture); }

        UIElement DelayRow(TextBox box, long step, Func<long> get, Action<long> set, string osd)
        {
            void Apply(long ms)
            {
                ms = Math.Clamp(ms, -600000, 600000);
                try { set(ms); } catch (Exception) { }
                box.Text = Secs(ms);
                main.ShowOsd(osd + MainWindow.DelayText(ms));
            }
            void Commit()
            {
                string t = box.Text.Trim().Replace(',', '.').Replace("s", "");
                if (double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out double v) && !double.IsNaN(v) && !double.IsInfinity(v)) Apply((long)Math.Round(v * 1000));
                else box.Text = Secs(SafeGet(get));
            }
            box.Text = Secs(SafeGet(get));
            box.KeyDown += (s, e) =>
            {
                if (e.Key == Key.Enter) { Commit(); box.SelectAll(); e.Handled = true; }
                else if (e.Key == Key.Up) { Apply(SafeGet(get) + step); e.Handled = true; }
                else if (e.Key == Key.Down) { Apply(SafeGet(get) - step); e.Handled = true; }
            };
            box.LostKeyboardFocus += (s, e) => Commit();
            var minus = new Button { Content = "\u2212", MinWidth = 34, Margin = new Thickness(6, 0, 0, 0), ToolTip = "Earlier" };
            minus.Click += (s, e) => Apply(SafeGet(get) - step);
            var plus = new Button { Content = "+", MinWidth = 34, Margin = new Thickness(4, 0, 0, 0), ToolTip = "Later" };
            plus.Click += (s, e) => Apply(SafeGet(get) + step);
            var reset = new Button { Content = "Reset", MinWidth = 60, Margin = new Thickness(8, 0, 0, 0) };
            reset.Click += (s, e) => Apply(0);
            return FxKit.Row(box, new TextBlock { Text = "seconds", Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, Style = FxKit.S("Dim") }, minus, plus, reset);
        }
        static long SafeGet(Func<long> get) { try { return get(); } catch (Exception) { return 0; } }

        // Keyboard shortcuts in the main window change delays and speed too: keep the boxes showing the real values.
        void RefreshSync()
        {
            if (audioDelay == null) return;
            if (!audioDelay.IsKeyboardFocusWithin) audioDelay.Text = Secs(SafeGet(() => main.Player.AudioDelayMs));
            if (!subDelay.IsKeyboardFocusWithin) subDelay.Text = Secs(SafeGet(() => main.Player.SubtitleDelayMs));
            if (!speed.IsMouseCaptureWithin && !speed.IsKeyboardFocusWithin)
            {
                double r = SafeRate();
                bool was = loading; loading = true;
                speed.Value = Math.Log2(Math.Clamp(r, 0.25, 4));
                speedText.Text = RateText(r);
                loading = was;
            }
        }
    }

    /// <summary>Picture adjustments (kept for the next files until Media Player closes).</summary>
    public sealed class PictureAdjustState
    {
        public bool On;
        public float[] Values = { 1f, 1f, 1f, 0f, 1f };   // brightness, contrast, saturation, hue, gamma
    }

    public partial class MainWindow
    {
        Window snapshotDialog;

        /// <summary>--snapshot scenes Effects[:tab], Info[:tab], Convert: the dialog is shown off the screen (never
        /// activated) and SaveSnapshot draws it instead of the main window.</summary>
        void SetupDialogScene(string name, string arg)
        {
            int.TryParse(arg, out int tab);
            Window w;
            if (name == "Effects")
            {
                Settings.Eq.On = true; Settings.Eq.Preset = -1; Settings.Eq.Preamp = 6;
                Settings.Eq.Bands = new float[] { 6, 4.5f, 3, 1, 0, -1, 0, 2, 3.5f, 4 };
                var e = new EffectsWindow(this);
                e.ShowTab(tab);
                w = e;
            }
            else if (name == "Info") { var m = new MediaInfoWindow(this); m.ShowTab(tab); w = m; }
            else w = new ConvertWindow(this);
            w.WindowStartupLocation = WindowStartupLocation.Manual;
            w.Left = -32000; w.Top = -32000;
            w.ShowActivated = false;
            w.Show();
            snapshotDialog = w;
        }

        EffectsWindow effectsWindow;
        public PictureAdjustState PictureAdjust { get; } = new PictureAdjustState();

        public void ApplyPictureAdjust()
        {
            var a = PictureAdjust;
            try { Player.SetAdjust(a.On, a.Values[0], a.Values[1], a.Values[2], a.Values[3], a.Values[4]); } catch (Exception) { }
        }

        public void ShowEffects(int tab)
        {
            if (effectsWindow == null)
            {
                effectsWindow = new EffectsWindow(this);
                effectsWindow.Closed += (s, e) => effectsWindow = null;
                effectsWindow.ShowTab(tab);
                effectsWindow.Show();
                return;
            }
            effectsWindow.ShowTab(tab);
            if (effectsWindow.WindowState == WindowState.Minimized) effectsWindow.WindowState = WindowState.Normal;
            effectsWindow.Activate();
        }
    }
}
