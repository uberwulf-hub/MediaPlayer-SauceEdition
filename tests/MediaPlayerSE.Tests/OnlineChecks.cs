// Watch online: which addresses open in Edge, reading YouTube's search page (a hand-made page in the shape of
// ytInitialData: nothing is fetched), recent searches, finding msedge.exe, and the window drawn off screen with
// made-up results. Never opens Edge or a browser, never reaches the network.
using System.IO;
using MediaPlayerSauce.Core;
using MediaPlayerSauce.Online;
using MediaPlayerSauce.UI;

namespace MediaPlayerSE.Tests
{
    static partial class Program
    {
        // A search page cut down to the parts the reader uses: three videos (one live, one with runs and a verified
        // channel, one with only the basics), something that isn't a video, and the "more results" token.
        const string YtPage = @"<!DOCTYPE html><html><head><script>var ytcfg={""INNERTUBE_CLIENT_VERSION"":""2.20251101.01.00""};</script></head><body>
<script nonce=""abc"">var ytInitialData = {""contents"":{""twoColumnSearchResultsRenderer"":{""primaryContents"":{""sectionListRenderer"":{""contents"":[
 {""itemSectionRenderer"":{""contents"":[
  {""videoRenderer"":{""videoId"":""abc123DEF45"",
    ""thumbnail"":{""thumbnails"":[{""url"":""https://i.ytimg.com/vi/abc123DEF45/hq720.jpg?sqp=small"",""width"":360},{""url"":""https://i.ytimg.com/vi/abc123DEF45/hq720.jpg?sqp=big"",""width"":720}]},
    ""title"":{""runs"":[{""text"":""How to bake ""},{""text"":""sourdough \u0026 rye""}]},
    ""longBylineText"":{""runs"":[{""text"":""Byline Kitchen""}]},
    ""ownerText"":{""runs"":[{""text"":""The Bread Kitchen""}]},
    ""publishedTimeText"":{""simpleText"":""3 years ago""},
    ""lengthText"":{""simpleText"":""12:34""},
    ""viewCountText"":{""simpleText"":""1,234,567 views""},
    ""shortViewCountText"":{""simpleText"":""1.2M views""},
    ""ownerBadges"":[{""metadataBadgeRenderer"":{""style"":""BADGE_STYLE_TYPE_VERIFIED"",""tooltip"":""Verified""}}],
    ""channelThumbnailSupportedRenderers"":{""channelThumbnailWithLinkRenderer"":{""thumbnail"":{""thumbnails"":[{""url"":""//yt3.ggpht.com/avatar1=s68""}]}}},
    ""detailedMetadataSnippets"":[{""snippetText"":{""runs"":[{""text"":""Bake a crusty ""},{""text"":""loaf"",""bold"":true},{""text"":"" at home.""}]}}]}},
  {""shelfRenderer"":{""title"":{""simpleText"":""People also watched""}}},
  {""videoRenderer"":{""videoId"":""LiVe0000001"",
    ""thumbnail"":{""thumbnails"":[{""url"":""https://i.ytimg.com/vi/LiVe0000001/hq720_live.jpg""}]},
    ""title"":{""runs"":[{""text"":""Harbour webcam - live""}]},
    ""ownerText"":{""runs"":[{""text"":""Harbour Cams""}]},
    ""viewCountText"":{""runs"":[{""text"":""321""},{""text"":"" watching""}]},
    ""badges"":[{""metadataBadgeRenderer"":{""style"":""BADGE_STYLE_TYPE_LIVE_NOW"",""label"":""LIVE""}}],
    ""thumbnailOverlays"":[{""thumbnailOverlayTimeStatusRenderer"":{""text"":{""simpleText"":""LIVE""},""style"":""LIVE""}}]}},
  {""videoRenderer"":{""videoId"":""plain000003"",
    ""title"":{""simpleText"":""Plain video""},
    ""longBylineText"":{""runs"":[{""text"":""Only Byline""}]},
    ""viewCountText"":{""simpleText"":""17 views""},
    ""lengthText"":{""simpleText"":""0:42""}}},
  {""videoRenderer"":{""title"":{""simpleText"":""No id, skipped""}}}
 ]}},
 {""continuationItemRenderer"":{""trigger"":""CONTINUATION_TRIGGER_ON_ITEM_SHOWN"",""continuationEndpoint"":{""continuationCommand"":{""token"":""EqNEXTtoken=="",""request"":""CONTINUATION_REQUEST_TYPE_SEARCH""}}}}
]}}}},""note"":""a } and a { inside a string"",""estimatedResults"":""1000""};</script>
<script>var other = {""videoRenderer"":{""videoId"":""notThisOne1""}};</script></body></html>";

        // the answer to a "more results" request (JSON only)
        const string YtMore = @"{""onResponseReceivedCommands"":[{""appendContinuationItemsAction"":{""continuationItems"":[
 {""itemSectionRenderer"":{""contents"":[
  {""videoRenderer"":{""videoId"":""abc123DEF45"",""title"":{""simpleText"":""Seen before""}}},
  {""videoRenderer"":{""videoId"":""next0000004"",""title"":{""simpleText"":""Page two""},""lengthText"":{""simpleText"":""1:02:03""}}}]}},
 {""continuationItemRenderer"":{""continuationEndpoint"":{""continuationCommand"":{""token"":""ThirdPage""}}}}]}}]}";

        // MEDIAPLAYERSE_PICTURES=<folder> keeps the drawn pictures (they are in the temporary folder otherwise)
        static string PictureDir()
        {
            string d = Environment.GetEnvironmentVariable("MEDIAPLAYERSE_PICTURES");
            if (string.IsNullOrEmpty(d)) return Dir("online");
            Directory.CreateDirectory(d);
            return d;
        }

        static void OnlineChecks()
        {
            // ---------------------------------------------------------------- which addresses open in Edge
            string[] web =
            {
                "https://www.youtube.com/watch?v=dQw4w9WgXcQ", "http://youtube.com/watch?v=abc&t=42s", "https://m.youtube.com/watch?v=abc",
                "https://youtu.be/dQw4w9WgXcQ", "https://youtu.be/dQw4w9WgXcQ?t=10", "https://www.youtube.com/shorts/abcDEF12345",
                "https://www.youtube.com/live/abcDEF12345", "https://music.youtube.com/watch?v=abc&list=RD", "https://www.youtube-nocookie.com/embed/abc",
                "https://www.youtube.com/playlist?list=PL123", "  https://www.youtube.com/watch?v=abc  ", "HTTPS://WWW.YOUTUBE.COM/watch?v=abc",
                "https://www.netflix.com/watch/80100172", "https://netflix.com/title/80057281", "https://www.netflix.com/browse",
                "https://www.primevideo.com/detail/0ABCDEF", "https://www.primevideo.com/region/eu/storefront", "https://www.amazon.com/gp/video/detail/B0ABC/",
                "https://www.amazon.co.uk/gp/video/storefront", "https://www.amazon.com/s?k=jack+ryan&i=instant-video", "https://www.amazon.de/Prime-Video/b?node=3010075031",
            };
            foreach (var u in web) Check("online: opens in Edge: " + u.Trim(), WebVideos.IsWebOnly(u));
            string[] notWeb =
            {
                "https://example.com/a.mp4", "http://radio.example/jazz", "rtsp://camera.local/stream", "rtmp://live.example/app", "mms://old.example/s",
                "udp://@239.0.0.1:1234", @"C:\Videos\youtube.com.mp4", "youtube.com/watch?v=abc", "ftp://www.youtube.com/watch?v=abc",
                "https://notyoutube.com/watch?v=abc", "https://youtube.com.evil.example/watch?v=abc", "https://www.amazon.com/dp/B0SOMETHING",
                "https://www.amazon.com/s?k=toaster", "https://netflix.com.example/watch/1", "https://example.com/youtube.com/watch", "", null, "   ",
                "https://youtu.be.example/abc", "file:///C:/Videos/a.mp4",
            };
            foreach (var u in notWeb) Check("online: plays in Media Player: " + (u ?? "(null)"), !WebVideos.IsWebOnly(u));
            Check("online: service names", WebVideos.ServiceOf("https://youtu.be/x") == "YouTube" && WebVideos.ServiceOf("https://www.netflix.com/watch/1") == "Netflix"
                  && WebVideos.ServiceOf("https://www.primevideo.com/") == "Prime" && WebVideos.ServiceOf("https://example.com/") == null);

            // the address is rebuilt (https, escaped) before it reaches Edge's command line
            Check("online: web address made https", WebVideos.WebUrl("http://www.youtube.com/watch?v=abc&t=4") == "https://www.youtube.com/watch?v=abc&t=4", WebVideos.WebUrl("http://www.youtube.com/watch?v=abc&t=4"));
            string tricky = WebVideos.WebUrl("https://www.youtube.com/results?search_query=a\" --disable-web-security \"b");
            Check("online: quotes can't break out of --app", tricky != null && !tricky.Contains('"') && !tricky.Contains(' '), tricky);
            Check("online: Edge arguments", WebVideos.EdgeArguments("https://www.youtube.com/watch?v=abc") == "--app=\"https://www.youtube.com/watch?v=abc\"");
            Check("online: not a web address", WebVideos.WebUrl("rtsp://x/y") == null && WebVideos.WebUrl("nonsense") == null);

            // finding msedge.exe reads the registry only and never throws (it may be missing on a test PC)
            string edge = null; Exception edgeError = null;
            try { edge = WebVideos.FindEdge(); } catch (Exception ex) { edgeError = ex; }
            Check("online: msedge lookup doesn't throw", edgeError == null, edgeError?.Message);
            Check("online: msedge lookup gives an existing file or nothing", edge == null || (File.Exists(edge) && edge.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)), edge);

            // Netflix and Prime Video addresses
            Check("online: Netflix search address", WebVideos.Netflix.SearchUrl(" the crown ") == "https://www.netflix.com/search?q=the%20crown", WebVideos.Netflix.SearchUrl(" the crown "));
            Check("online: Prime search address", WebVideos.Prime.SearchUrl("jack & ryan") == "https://www.amazon.com/s?k=jack%20%26%20ryan&i=instant-video", WebVideos.Prime.SearchUrl("jack & ryan"));
            Check("online: the services' own addresses open in Edge", WebVideos.IsWebOnly(WebVideos.Netflix.Home) && WebVideos.IsWebOnly(WebVideos.Prime.Home)
                  && WebVideos.IsWebOnly(WebVideos.Netflix.SearchUrl("x")) && WebVideos.IsWebOnly(WebVideos.Prime.SearchUrl("x")));
            Check("online: YouTube search address", YouTubeSearch.SearchUrl("  cats & dogs ") == "https://www.youtube.com/results?search_query=cats%20%26%20dogs");
            Check("online: YouTube search address opens in Edge", WebVideos.IsWebOnly(YouTubeSearch.SearchUrl("x")));

            // ---------------------------------------------------------------- reading the search page
            var found = YouTubeSearch.ParseHtml(YtPage, out string token);
            Check("online: three videos found, in page order", found.Count == 3 && found[0].Id == "abc123DEF45" && found[1].Id == "LiVe0000001" && found[2].Id == "plain000003",
                  string.Join(",", found.Select(v => v.Id)));
            Check("online: the continuation token", token == "EqNEXTtoken==", token);
            var a = found[0];
            Check("online: title from runs (escapes read)", a.Title == "How to bake sourdough & rye", a.Title);
            Check("online: channel from ownerText first", a.Channel == "The Bread Kitchen", a.Channel);
            Check("online: short view count preferred", a.Views == "1.2M views", a.Views);
            Check("online: age", a.Age == "3 years ago", a.Age);
            Check("online: length and badge", a.Length == "12:34" && a.Badge == "12:34" && a.HasBadge && !a.IsLive);
            Check("online: verified channel", a.Verified);
            Check("online: biggest thumbnail", a.Thumb == "https://i.ytimg.com/vi/abc123DEF45/hq720.jpg?sqp=big", a.Thumb);
            Check("online: avatar made https", a.Avatar == "https://yt3.ggpht.com/avatar1=s68", a.Avatar);
            Check("online: snippet from runs", a.Snippet == "Bake a crusty loaf at home." && a.HasSnippet, a.Snippet);
            Check("online: meta line", a.Meta == "1.2M views " + YouTubeVideo.MiddleDot + " 3 years ago", a.Meta);
            Check("online: watch address", a.Url == "https://www.youtube.com/watch?v=abc123DEF45" && WebVideos.IsWebOnly(a.Url));
            var live = found[1];
            Check("online: live video", live.IsLive && live.Badge == "LIVE" && live.HasBadge && live.Length == "", live.Badge);
            Check("online: live viewers as views", live.Views == "321 watching" && live.Meta == "321 watching", live.Meta);
            Check("online: not verified", !live.Verified);
            var plain = found[2];
            Check("online: channel from the byline", plain.Channel == "Only Byline", plain.Channel);
            Check("online: plain title, views", plain.Title == "Plain video" && plain.Views == "17 views" && plain.Age == "", plain.Title);
            Check("online: missing thumbnail falls back", plain.Thumb == "https://i.ytimg.com/vi/plain000003/mqdefault.jpg", plain.Thumb);
            Check("online: no avatar, no snippet", plain.Avatar == null && plain.Snippet == "" && !plain.HasSnippet);
            Check("online: client version read", YouTubeSearch.ClientVersionIn(YtPage) == "2.20251101.01.00", YouTubeSearch.ClientVersionIn(YtPage));
            Check("online: no client version", YouTubeSearch.ClientVersionIn("<html></html>") == null);

            // window["ytInitialData"] = {...} is read too
            var other = YouTubeSearch.ParseHtml("<script>window[\"ytInitialData\"] = {\"a\":[{\"videoRenderer\":{\"videoId\":\"winForm0001\",\"title\":{\"simpleText\":\"W\"}}}]};</script>", out string t2);
            Check("online: window[\"ytInitialData\"] form", other.Count == 1 && other[0].Id == "winForm0001" && t2 == "");
            // a page without results data (a consent page, or YouTube changed it) is a problem, not an empty list
            bool threw = false;
            try { YouTubeSearch.ParseHtml("<html><body>Before you continue to YouTube</body></html>", out _); } catch (InvalidDataException) { threw = true; }
            Check("online: a page without results data is reported", threw);
            Check("online: empty results page", YouTubeSearch.ParseHtml("<script>var ytInitialData = {\"contents\":{}};</script>", out string t3).Count == 0 && t3 == "");

            // the "more results" answer
            var more = YouTubeSearch.ParseJson(YtMore, out string t4);
            Check("online: more results read", more.Count == 2 && more[1].Id == "next0000004" && more[1].Length == "1:02:03" && t4 == "ThirdPage", t4);

            // ---------------------------------------------------------------- recent searches
            var recent = new List<string> { "a", "b", "c", "d", "e", "f", "g", "h" };
            YouTubeSearch.Remember(recent, " cats ", Settings.MaxYtRecent);
            Check("online: recent search first, at most 8", recent.Count == 8 && recent[0] == "cats" && recent[7] == "g", string.Join(",", recent));
            YouTubeSearch.Remember(recent, "C", Settings.MaxYtRecent);
            Check("online: a repeated search moves to the top", recent.Count == 8 && recent[0] == "C" && recent.Count(r => r.Equals("c", StringComparison.OrdinalIgnoreCase)) == 1, string.Join(",", recent));
            YouTubeSearch.Remember(recent, "   ", Settings.MaxYtRecent);
            Check("online: empty searches aren't kept", recent[0] == "C");

            // ---------------------------------------------------------------- the window, drawn off screen
            var settings = new Settings();
            settings.YtRecent = new List<string> { "sourdough", "harbour webcam" };
            var opened = new List<string>();
            int saves = 0;
            var w = new OnlineWindow(settings, () => saves++, u => opened.Add(u));
            Check("online: window starts on YouTube", w.Source == "YouTube");
            w.Start("Netflix", null);
            Check("online: source Netflix picks its tab", w.Source == "Netflix");
            w.Start("Prime", null);
            Check("online: source Prime picks its tab", w.Source == "Prime");
            w.Start("YouTube", null);
            Check("online: back to YouTube", w.Source == "YouTube" && opened.Count == 0);
            string png1 = Path.Combine(PictureDir(), "online-home.png");
            w.SaveSnapshot(png1, 1000, 700);
            Check("online: home drawn", File.Exists(png1) && new FileInfo(png1).Length > 1000);
            // made-up results with no pictures (pictures would be downloaded)
            foreach (var v in found) { v.Thumb = null; v.Avatar = null; }
            w.ShowDemo(found, "sourdough");
            string png2 = Path.Combine(PictureDir(), "online-results.png");
            w.SaveSnapshot(png2, 1000, 700);
            Check("online: results drawn", File.Exists(png2) && new FileInfo(png2).Length > 1000 && w.Results.Count == 3);
            w.Start("Prime", null);
            string png3 = Path.Combine(PictureDir(), "online-prime.png");
            w.SaveSnapshot(png3, 1000, 700);
            Check("online: Prime Video page drawn", File.Exists(png3));
            Check("online: nothing opened, nothing saved", opened.Count == 0 && saves == 0);
            w.Close();
            Console.WriteLine("  (pictures in " + PictureDir() + ")");
        }
    }
}
