// Watch online: YouTube search (results like YouTube's search page, more as you scroll), Netflix and Prime Video.
// Videos and the services open in their own Edge window (Online.WebVideos.OpenInEdge); Media Player pauses meanwhile.
// Searches run off the UI thread (Online.YouTubeSearch); the window awaits them here, on the UI thread.
using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MediaPlayerSauce.Core;
using MediaPlayerSauce.Online;

namespace MediaPlayerSauce.UI
{
    public partial class MainWindow
    {
        OnlineWindow online;

        /// <summary>Opens Watch online. source "Netflix" / "Prime" picks that tab; search starts a YouTube search.</summary>
        public void ShowOnline(string source, string search)
        {
            if (Quiet) { Console.Error.WriteLine("watch online: " + source + " " + search); return; }   // snapshot runs open no windows
            if (online == null)
            {
                online = new OnlineWindow(Settings, SaveSoon, url => WebVideos.OpenInEdge(url, this)) { Owner = this };
                online.Closed += (s, e) => { if (s == online) online = null; };
                online.Show();
            }
            else
            {
                if (online.WindowState == WindowState.Minimized) online.WindowState = WindowState.Normal;
                online.Activate();
            }
            online.Start(source, search);
        }
    }

    /// <summary>Loads web pictures (thumbnails, channel pictures) without blocking: WPF downloads http images in the
    /// background. The parameter is the width to decode at. Pictures are kept for the window's life (at most 400).</summary>
    public sealed class WebImageConverter : IValueConverter
    {
        readonly Dictionary<string, ImageSource> cache = new Dictionary<string, ImageSource>();

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            string url = value as string;
            if (string.IsNullOrWhiteSpace(url)) return null;
            int width = 0;
            if (parameter != null) int.TryParse(parameter.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out width);
            string key = width + "|" + url;
            if (cache.TryGetValue(key, out var img)) return img;
            try
            {
                if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return null;
                if (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp && !uri.IsFile) return null;
                var bi = new BitmapImage();
                bi.BeginInit();
                bi.UriSource = uri;
                if (width > 0) bi.DecodePixelWidth = width;
                bi.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
                bi.CacheOption = uri.IsFile ? BitmapCacheOption.OnLoad : BitmapCacheOption.Default;
                bi.EndInit();
                bi.DownloadFailed += (s, e) => { };
                bi.DecodeFailed += (s, e) => { };
                if (cache.Count > 400) cache.Clear();
                cache[key] = bi;
                return bi;
            }
            catch (Exception) { return null; }
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
    }

    public partial class OnlineWindow : Window
    {
        const char Lq = (char)0x201C, Rq = (char)0x201D;   // curly quotes
        readonly Settings settings;
        readonly Action saveSoon;
        readonly Action<string> openUrl;
        /// <summary>The results shown (YouTube only).</summary>
        public ObservableCollection<YouTubeVideo> Results { get; } = new ObservableCollection<YouTubeVideo>();
        YouTubeSearch job, moreJob, last;
        CancellationTokenSource cancel = new CancellationTokenSource();
        string status = "";
        public bool WasClosed { get; private set; }

        /// <summary>openUrl opens an address in its own window (WebVideos.OpenInEdge in the app; tests pass their own).</summary>
        public OnlineWindow(Settings settings, Action saveSoon, Action<string> openUrl)
        {
            this.settings = settings;
            this.saveSoon = saveSoon ?? (() => { });
            this.openUrl = openUrl ?? (u => { });
            // the theme comes from the app; tests have no app, so the window brings its own copy then
            if (Application.Current == null || Application.Current.TryFindResource("TabPill") == null)
                Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("pack://application:,,,/MediaPlayerSE;component/UI/Theme.xaml") });
            Resources["WebImage"] = new WebImageConverter();
            InitializeComponent();

            LstResults.ItemsSource = Results;
            TabYouTube.Checked += (s, e) => UpdateView();
            TabNetflix.Checked += (s, e) => UpdateView();
            TabPrime.Checked += (s, e) => UpdateView();
            TxtSearch.TextChanged += (s, e) => UpdateView();
            TxtSearch.PreviewKeyDown += OnSearchKey;
            BtnSearch.Click += (s, e) => Search();
            BtnClear.Click += (s, e) => { TxtSearch.Text = ""; TxtSearch.Focus(); };
            BtnClose.Click += (s, e) => Close();
            BtnWeb.Click += (s, e) => { string q = TxtSearch.Text.Trim(); if (q.Length > 0) Open(YouTubeSearch.SearchUrl(q)); };
            BtnStreamOpen.Click += (s, e) => { var st = Stream; if (st != null) Open(st.Home); };
            BtnStreamSearch.Click += (s, e) => { var st = Stream; string q = TxtSearch.Text.Trim(); if (st != null && q.Length > 0) Open(st.SearchUrl(q)); };
            LstResults.PreviewMouseLeftButtonUp += (s, e) =>
            {
                if (ItemsControl.ContainerFromElement(LstResults, e.OriginalSource as DependencyObject) is ListBoxItem item && item.DataContext is YouTubeVideo v) OpenVideo(v);
            };
            LstResults.KeyDown += (s, e) => { if (e.Key == Key.Enter && LstResults.SelectedItem is YouTubeVideo v) { e.Handled = true; OpenVideo(v); } };
            // more results when the list is scrolled near its end, as on YouTube
            LstResults.AddHandler(ScrollViewer.ScrollChangedEvent, new ScrollChangedEventHandler((s, e) =>
            {
                if (e.ExtentHeight > 0 && e.VerticalOffset + e.ViewportHeight >= e.ExtentHeight - 600) RequestMore();
            }));
            PreviewKeyDown += (s, e) =>
            {
                if (e.Key == Key.Escape) { e.Handled = true; Close(); }
                else if ((e.Key == Key.Oem2 || e.Key == Key.Divide) && !TxtSearch.IsKeyboardFocusWithin) { e.Handled = true; TxtSearch.Focus(); TxtSearch.SelectAll(); }
            };
            Closed += (s, e) => { WasClosed = true; try { cancel.Cancel(); } catch (Exception) { } };
            Loaded += (s, e) => { TxtSearch.Focus(); TxtSearch.SelectAll(); };
            UpdateView();
        }

        /// <summary>The selected tab: "YouTube", "Netflix" or "Prime".</summary>
        public string Source => TabNetflix.IsChecked == true ? "Netflix" : TabPrime.IsChecked == true ? "Prime" : "YouTube";
        StreamService Stream => Source == "Netflix" ? WebVideos.Netflix : Source == "Prime" ? WebVideos.Prime : null;
        public string StatusText => status;

        /// <summary>Picks the tab (source) and, on YouTube, searches for search.</summary>
        public void Start(string source, string search)
        {
            if (string.Equals(source, "Netflix", StringComparison.OrdinalIgnoreCase)) TabNetflix.IsChecked = true;
            else if (string.Equals(source, "Prime", StringComparison.OrdinalIgnoreCase) || string.Equals(source, "Prime Video", StringComparison.OrdinalIgnoreCase)) TabPrime.IsChecked = true;
            else if (!string.IsNullOrEmpty(source) || !string.IsNullOrWhiteSpace(search)) TabYouTube.IsChecked = true;
            if (!string.IsNullOrWhiteSpace(search))
            {
                TxtSearch.Text = search.Trim();
                if (Source == "YouTube") Search();
            }
            UpdateView();
        }

        void OnSearchKey(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) { e.Handled = true; Search(); }
            else if (e.Key == Key.Escape && TxtSearch.Text.Length > 0) { e.Handled = true; TxtSearch.Text = ""; }
            else if (e.Key == Key.Down && Results.Count > 0 && LstResults.IsVisible)
            {
                e.Handled = true;
                LstResults.SelectedIndex = 0;
                (LstResults.ItemContainerGenerator.ContainerFromIndex(0) as ListBoxItem)?.Focus();
            }
        }

        void Open(string url)
        {
            openUrl(url);
            SetStatus((WebVideos.ServiceOf(url) == "Netflix" ? "Netflix" : WebVideos.ServiceOf(url) == "Prime" ? "Prime Video" : "YouTube")
                      + " opened in its own window.", false, true);
        }

        void OpenVideo(YouTubeVideo v) { if (v != null && !string.IsNullOrEmpty(v.Id)) Open(v.Url); }

        /// <summary>Searches what's in the box: YouTube here, Netflix / Prime Video in their own window.</summary>
        public void Search()
        {
            string q = TxtSearch.Text.Trim();
            if (q.Length == 0) { TxtSearch.Focus(); return; }
            var st = Stream;
            if (st != null) { Open(st.SearchUrl(q)); return; }
            YouTubeSearch.Remember(settings.YtRecent, q, Settings.MaxYtRecent);
            saveSoon();
            try { cancel.Cancel(); } catch (Exception) { }
            cancel = new CancellationTokenSource();
            Results.Clear(); last = null; moreJob = null;
            var s = YouTubeSearch.Start(q, cancel.Token);
            job = s;
            SetStatus("Searching YouTube for " + Lq + q + Rq + "...", false);
            UpdateView();
            _ = FinishSearch(s);
        }

        async Task FinishSearch(YouTubeSearch s)
        {
            await s.Done;   // back on the UI thread
            if (s != job || WasClosed) return;   // a newer search took over, or the window closed
            ShowResults(s);
        }

        /// <summary>Shows a finished search (public for tests).</summary>
        public void ShowResults(YouTubeSearch s)
        {
            job = null;
            if (s.Error != null) SetStatus("Search isn't working right now. " + s.Error, true);
            else if (s.Results.Count == 0) SetStatus("No results for " + Lq + s.Query + Rq + ".", true);
            else
            {
                SetStatus("", false);
                foreach (var v in s.Results) Results.Add(v);
                last = s;
                LstResults.ScrollIntoView(Results[0]);
            }
            UpdateView();
        }

        /// <summary>Shows results without searching (tests and snapshots).</summary>
        public void ShowDemo(IEnumerable<YouTubeVideo> videos, string query)
        {
            TxtSearch.Text = query ?? "";
            Results.Clear();
            foreach (var v in videos) Results.Add(v);
            SetStatus("", false);
            UpdateView();
        }

        void RequestMore()
        {
            if (last == null || !last.HasMore || moreJob != null || job != null) return;
            var m = YouTubeSearch.More(last, cancel.Token);
            moreJob = m;
            _ = FinishMore(m);
        }

        async Task FinishMore(YouTubeSearch m)
        {
            await m.Done;
            if (m != moreJob || WasClosed) return;
            moreJob = null;
            if (m.Error != null) return;   // a failed "more" just stops; scrolling again retries
            foreach (var v in m.Results) Results.Add(v);
            last = m;
        }

        void SetStatus(string text, bool web, bool keepView = false)
        {
            status = text ?? "";
            LblStatus.Text = status;
            BtnWeb.Visibility = web ? Visibility.Visible : Visibility.Collapsed;
            if (!keepView) UpdateView();
            else PnlStatus.Visibility = status.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        void UpdateView()
        {
            if (LblHint == null) return;   // still loading
            bool empty = TxtSearch.Text.Length == 0;
            LblHint.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
            BtnClear.Visibility = empty ? Visibility.Collapsed : Visibility.Visible;
            var st = Stream;
            PnlStatus.Visibility = status.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            if (st == null)
            {
                LblHint.Text = "Search YouTube";
                BtnWeb.Content = "Open the search on youtube.com";
                PnlStream.Visibility = Visibility.Collapsed;
                bool searched = Results.Count > 0 || job != null || BtnWeb.Visibility == Visibility.Visible;
                LstResults.Visibility = Results.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
                PnlHome.Visibility = searched ? Visibility.Collapsed : Visibility.Visible;
                UpdateRecent();
            }
            else
            {
                LblHint.Text = "Search " + st.Name;
                LstResults.Visibility = Visibility.Collapsed; PnlHome.Visibility = Visibility.Collapsed;
                if (BtnWeb.Visibility == Visibility.Visible) PnlStatus.Visibility = Visibility.Collapsed;   // YouTube's problems stay on YouTube's tab
                PnlStream.Visibility = Visibility.Visible;
                LblStreamName.Text = st.Name;
                LblStreamName.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(st.Color));
                LblStreamText.Text = st.Text;
                BtnStreamOpen.Content = "Open " + st.Name;
                string q = TxtSearch.Text.Trim();
                BtnStreamSearch.Visibility = q.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
                BtnStreamSearch.Content = "Search " + st.Name + " for " + Lq + (q.Length > 40 ? q.Substring(0, 40) + "..." : q) + Rq + "";
            }
        }

        void UpdateRecent()
        {
            PnlRecent.Children.Clear();
            foreach (string r in settings.YtRecent.Take(Settings.MaxYtRecent))
            {
                var b = new Button { Content = r, Tag = r, Margin = new Thickness(0, 0, 8, 8), Padding = new Thickness(14, 6, 14, 6), ToolTip = "Search YouTube for this again" };
                b.Click += (s, e) => { TxtSearch.Text = (string)((Button)s).Tag; Search(); };
                PnlRecent.Children.Add(b);
            }
            LblRecent.Visibility = PnlRecent.Children.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        /// <summary>Draws the window's content into a PNG without showing it (tests).</summary>
        public void SaveSnapshot(string png, double width, double height)
        {
            var root = (FrameworkElement)Content;
            root.Measure(new Size(width, height));
            root.Arrange(new Rect(0, 0, width, height));
            root.UpdateLayout();
            var bmp = new RenderTargetBitmap((int)width, (int)height, 96, 96, PixelFormats.Pbgra32);
            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                dc.DrawRectangle((Brush)FindResource("Panel"), null, new Rect(0, 0, width, height));
            }
            bmp.Render(dv);
            bmp.Render(root);
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(bmp));
            using var f = System.IO.File.Create(png);
            enc.Save(f);
        }
    }
}
