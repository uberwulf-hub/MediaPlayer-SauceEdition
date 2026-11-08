// YouTube search: reads the results youtube.com shows for a search (the page's ytInitialData, and the same "more
// results" request the page makes). No API key. Ported from App Hub's Media Player (Engine\Online.cs).
// Searches run off the UI thread; the window awaits Done on the UI thread, so results reach it through the Dispatcher.

using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace MediaPlayerSauce.Online
{
    /// <summary>One search result, shaped for the result list's bindings.</summary>
    public sealed class YouTubeVideo
    {
        public string Id { get; set; }
        public string Title { get; set; }
        public string Channel { get; set; }
        public bool Verified { get; set; }
        public string Views { get; set; }
        public string Age { get; set; }
        public string Length { get; set; }
        public bool IsLive { get; set; }
        public string Thumb { get; set; }
        public string Avatar { get; set; }
        public string Snippet { get; set; }
        public string Url => "https://www.youtube.com/watch?v=" + Id;
        public const char MiddleDot = (char)0xB7;
        /// <summary>"1.2M views, 3 years ago" with a middle dot, as YouTube writes it under the title.</summary>
        public string Meta
        {
            get
            {
                var parts = new List<string>();
                if (!string.IsNullOrEmpty(Views)) parts.Add(Views);
                if (!string.IsNullOrEmpty(Age)) parts.Add(Age);
                return string.Join(" " + MiddleDot + " ", parts);
            }
        }
        /// <summary>Text of the corner badge on the thumbnail: the length, or LIVE.</summary>
        public string Badge => IsLive ? "LIVE" : Length;
        public bool HasBadge => IsLive || !string.IsNullOrEmpty(Length);
        public bool HasSnippet => !string.IsNullOrEmpty(Snippet);
    }

    public sealed class YouTubeSearch
    {
        const string UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0.0.0 Safari/537.36 Edg/130.0.0.0";
        // SOCS=CAI: "reject all" on Google's cookie consent, so no consent page comes back instead of results
        const string Cookies = "SOCS=CAI; CONSENT=PENDING+987";
        static readonly JsonDocumentOptions JsonOptions = new JsonDocumentOptions { MaxDepth = 1024, AllowTrailingCommas = true };

        static readonly Lazy<HttpClient> Http = new Lazy<HttpClient>(() =>
        {
            var h = new HttpClientHandler { AutomaticDecompression = DecompressionMethods.All, UseCookies = false, AllowAutoRedirect = true };
            var c = new HttpClient(h) { Timeout = TimeSpan.FromSeconds(20) };
            c.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", UserAgent);
            c.DefaultRequestHeaders.TryAddWithoutValidation("Accept-Language", "en-US,en;q=0.9");
            c.DefaultRequestHeaders.TryAddWithoutValidation("Cookie", Cookies);
            return c;
        });

        public string Query { get; }
        /// <summary>This search's results (for More: only the new page).</summary>
        public List<YouTubeVideo> Results { get; } = new List<YouTubeVideo>();
        /// <summary>A sentence for the person when the search failed, else null.</summary>
        public string Error { get; private set; }
        /// <summary>True when YouTube couldn't be reached at all (no internet), false when its page couldn't be read.</summary>
        public bool Offline { get; private set; }
        /// <summary>Token for the next page of results (empty when there are no more).</summary>
        public string Continuation { get; private set; } = "";
        public bool HasMore => !string.IsNullOrEmpty(Continuation);
        public bool IsDone => Done.IsCompleted;
        /// <summary>Completes (never faults) when the search has finished, well or not.</summary>
        public Task Done { get; private set; } = Task.CompletedTask;
        string clientVersion = "2.20241010.00.00";
        readonly HashSet<string> seen = new HashSet<string>();

        YouTubeSearch(string query) { Query = (query ?? "").Trim(); }

        /// <summary>Starts a search off the UI thread.</summary>
        public static YouTubeSearch Start(string query, CancellationToken cancel = default)
        {
            var s = new YouTubeSearch(query);
            s.Done = Task.Run(() => s.Run(null, cancel));
            return s;
        }

        /// <summary>Starts loading the next page of an earlier search. Results holds only the new page.</summary>
        public static YouTubeSearch More(YouTubeSearch previous, CancellationToken cancel = default)
        {
            var s = new YouTubeSearch(previous.Query) { clientVersion = previous.clientVersion };
            foreach (var id in previous.seen) s.seen.Add(id);
            string token = previous.Continuation;
            s.Done = Task.Run(() => s.Run(token, cancel));
            return s;
        }

        /// <summary>The address of the same search on youtube.com (for "Open the search on youtube.com").</summary>
        public static string SearchUrl(string query)
        {
            return "https://www.youtube.com/results?search_query=" + Uri.EscapeDataString((query ?? "").Trim());
        }

        /// <summary>Puts a search first in the recent list (no repeats, at most max).</summary>
        public static void Remember(List<string> recent, string query, int max)
        {
            query = (query ?? "").Trim();
            if (query.Length == 0 || recent == null) return;
            recent.RemoveAll(r => string.Equals(r, query, StringComparison.CurrentCultureIgnoreCase));
            recent.Insert(0, query);
            if (recent.Count > max) recent.RemoveRange(max, recent.Count - max);
        }

        async Task Run(string token, CancellationToken cancel)
        {
            try
            {
                var found = new List<YouTubeVideo>();
                string next;
                if (token == null)
                {
                    string html = await Http.Value.GetStringAsync(SearchUrl(Query) + "&hl=en&gl=US", cancel).ConfigureAwait(false);
                    string v = ClientVersionIn(html);
                    if (v != null) clientVersion = v;
                    found = ParseHtml(html, out next);
                }
                else
                {
                    var body = new JsonObject
                    {
                        ["context"] = new JsonObject { ["client"] = new JsonObject { ["clientName"] = "WEB", ["clientVersion"] = clientVersion, ["hl"] = "en", ["gl"] = "US" } },
                        ["continuation"] = token,
                    };
                    using var content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
                    using var resp = await Http.Value.PostAsync("https://www.youtube.com/youtubei/v1/search?prettyPrint=false", content, cancel).ConfigureAwait(false);
                    resp.EnsureSuccessStatusCode();
                    string json = await resp.Content.ReadAsStringAsync(cancel).ConfigureAwait(false);
                    found = ParseJson(json, out next);
                }
                foreach (var v in found) if (seen.Add(v.Id)) Results.Add(v);
                Continuation = next;
            }
            catch (OperationCanceledException) when (cancel.IsCancellationRequested)
            {
                Error = "The search was stopped.";
            }
            catch (Exception ex) when (ex is HttpRequestException || ex is TaskCanceledException || ex is IOException)
            {
                Offline = true;
                Error = "YouTube couldn't be reached. Check that this computer is connected to the internet, then try again.";
            }
            catch (Exception)
            {
                Error = "YouTube's search page couldn't be read. YouTube may have changed it.";
            }
        }

        // ---------------------------------------------------------------- page parsing (public for tests)
        /// <summary>Finds the results in a search page's HTML (its ytInitialData).</summary>
        public static List<YouTubeVideo> ParseHtml(string html, out string continuation)
        {
            string json = ExtractJson(html ?? "", "ytInitialData");
            if (json == null) throw new InvalidDataException("no results data in the page");
            return ParseJson(json, out continuation);
        }

        /// <summary>Finds the results in JSON: ytInitialData, or the answer to a "more results" request.</summary>
        public static List<YouTubeVideo> ParseJson(string json, out string continuation)
        {
            var found = new List<YouTubeVideo>();
            continuation = "";
            using var doc = JsonDocument.Parse(json, JsonOptions);
            Walk(doc.RootElement, found, ref continuation, 0);
            return found;
        }

        /// <summary>The page's INNERTUBE_CLIENT_VERSION (sent with "more results" requests), or null.</summary>
        public static string ClientVersionIn(string html)
        {
            const string key = "\"INNERTUBE_CLIENT_VERSION\":\"";
            int v = html.IndexOf(key, StringComparison.Ordinal);
            if (v < 0) return null;
            v += key.Length;
            int e = html.IndexOf('"', v);
            return e > v && e - v < 40 ? html.Substring(v, e - v) : null;
        }

        /// <summary>The JSON object assigned to name in a script (var x = {...}; or window["x"] = {...};).</summary>
        public static string ExtractJson(string html, string name)
        {
            int at = html.IndexOf(name, StringComparison.Ordinal);
            while (at >= 0)
            {
                int i = at + name.Length;
                while (i < html.Length && (html[i] == ' ' || html[i] == '"' || html[i] == ']' || html[i] == '\'')) i++;
                if (i < html.Length && html[i] == '=')
                {
                    i++;
                    while (i < html.Length && char.IsWhiteSpace(html[i])) i++;
                    if (i < html.Length && html[i] == '{')
                    {
                        int end = MatchBrace(html, i);
                        if (end > i) return html.Substring(i, end - i + 1);
                    }
                }
                at = html.IndexOf(name, at + name.Length, StringComparison.Ordinal);
            }
            return null;
        }

        static int MatchBrace(string s, int start)
        {
            int depth = 0;
            bool str = false, esc = false;
            for (int i = start; i < s.Length; i++)
            {
                char c = s[i];
                if (str)
                {
                    if (esc) esc = false;
                    else if (c == '\\') esc = true;
                    else if (c == '"') str = false;
                    continue;
                }
                if (c == '"') str = true;
                else if (c == '{') depth++;
                else if (c == '}') { depth--; if (depth == 0) return i; }
            }
            return -1;
        }

        /// <summary>Collects every videoRenderer (in page order) and the last continuation token.</summary>
        static void Walk(JsonElement node, List<YouTubeVideo> found, ref string continuation, int depth)
        {
            if (depth > 200) return;
            if (node.ValueKind == JsonValueKind.Object)
            {
                foreach (var kv in node.EnumerateObject())
                {
                    if (kv.Name == "videoRenderer")
                    {
                        var v = ReadVideo(kv.Value);
                        if (v != null) found.Add(v);
                    }
                    else if (kv.Name == "continuationItemRenderer")   // the "more results" marker at the end of the list
                    {
                        string t = Str(Get(kv.Value, "continuationEndpoint", "continuationCommand", "token"));
                        if (!string.IsNullOrEmpty(t)) continuation = t;
                    }
                    else Walk(kv.Value, found, ref continuation, depth + 1);
                }
            }
            else if (node.ValueKind == JsonValueKind.Array)
                foreach (var x in node.EnumerateArray()) Walk(x, found, ref continuation, depth + 1);
        }

        static YouTubeVideo ReadVideo(JsonElement r)
        {
            if (r.ValueKind != JsonValueKind.Object) return null;
            string id = Str(Get(r, "videoId"));
            if (string.IsNullOrEmpty(id)) return null;
            var v = new YouTubeVideo { Id = id };
            v.Title = Text(Get(r, "title"));
            v.Channel = Text(Get(r, "ownerText"));
            if (string.IsNullOrEmpty(v.Channel)) v.Channel = Text(Get(r, "longBylineText"));
            v.Age = Text(Get(r, "publishedTimeText"));
            v.Length = Text(Get(r, "lengthText"));
            v.Views = Text(Get(r, "shortViewCountText"));
            if (string.IsNullOrEmpty(v.Views)) v.Views = Text(Get(r, "viewCountText"));
            // live now: a LIVE badge or thumbnail overlay somewhere in the result, or "123 watching" instead of views
            v.IsLive = HasValue(r, "BADGE_STYLE_TYPE_LIVE_NOW", 0) || HasValue(Get(r, "thumbnailOverlays"), "LIVE", 0)
                || (string.IsNullOrEmpty(v.Length) && Text(Get(r, "viewCountText")).Contains("watching"));
            if (v.IsLive) v.Length = "";
            foreach (var b in Arr(Get(r, "ownerBadges")))
            {
                string style = Str(Get(b, "metadataBadgeRenderer", "style"));
                if (style.StartsWith("BADGE_STYLE_TYPE_VERIFIED", StringComparison.Ordinal)) v.Verified = true;
            }
            var thumbs = Arr(Get(r, "thumbnail", "thumbnails"));
            v.Thumb = thumbs.Count > 0 ? FixUrl(Str(Get(thumbs[thumbs.Count - 1], "url"))) : "";
            if (string.IsNullOrEmpty(v.Thumb)) v.Thumb = "https://i.ytimg.com/vi/" + id + "/mqdefault.jpg";
            var av = Arr(Get(r, "channelThumbnailSupportedRenderers", "channelThumbnailWithLinkRenderer", "thumbnail", "thumbnails"));
            v.Avatar = av.Count > 0 ? FixUrl(Str(Get(av[0], "url"))) : null;
            if (v.Avatar == "") v.Avatar = null;
            var snips = Arr(Get(r, "detailedMetadataSnippets"));
            v.Snippet = snips.Count > 0 ? Text(Get(snips[0], "snippetText")) : "";
            return v;
        }

        // ---------------------------------------------------------------- JSON helpers
        static JsonElement Get(JsonElement o, params string[] keys)
        {
            foreach (var k in keys)
            {
                if (o.ValueKind != JsonValueKind.Object || !o.TryGetProperty(k, out var v)) return default;
                o = v;
            }
            return o;
        }
        static bool HasValue(JsonElement o, string value, int depth)
        {
            if (depth > 30) return false;
            switch (o.ValueKind)
            {
                case JsonValueKind.String: return o.GetString() == value;
                case JsonValueKind.Object: foreach (var kv in o.EnumerateObject()) if (HasValue(kv.Value, value, depth + 1)) return true; return false;
                case JsonValueKind.Array: foreach (var x in o.EnumerateArray()) if (HasValue(x, value, depth + 1)) return true; return false;
                default: return false;
            }
        }
        static List<JsonElement> Arr(JsonElement o)
        {
            return o.ValueKind == JsonValueKind.Array ? o.EnumerateArray().ToList() : new List<JsonElement>();
        }
        static string Str(JsonElement o)
        {
            switch (o.ValueKind)
            {
                case JsonValueKind.String: return o.GetString() ?? "";
                case JsonValueKind.Number: return o.GetRawText();
                case JsonValueKind.True: return "True";
                case JsonValueKind.False: return "False";
                default: return "";
            }
        }
        /// <summary>YouTube text: { simpleText } or { runs: [ { text } ] }.</summary>
        static string Text(JsonElement o)
        {
            if (o.ValueKind != JsonValueKind.Object) return "";
            string s = Str(Get(o, "simpleText"));
            if (s.Length > 0) return s;
            var sb = new StringBuilder();
            foreach (var run in Arr(Get(o, "runs"))) sb.Append(Str(Get(run, "text")));
            return sb.ToString();
        }
        static string FixUrl(string u) { return u.StartsWith("//", StringComparison.Ordinal) ? "https:" + u : u; }
    }
}
