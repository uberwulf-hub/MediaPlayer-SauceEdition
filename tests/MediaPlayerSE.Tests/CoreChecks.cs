using System.IO;
using System.Text;
using System.Text.Json.Nodes;
using MediaPlayerSauce.Core;

namespace MediaPlayerSE.Tests
{
    static partial class Program
    {
        static void FileChecks()
        {
            Check("files: a web address is a URL", MediaFiles.IsUrl("https://example.com/a.mp4") && MediaFiles.IsUrl("dvd:///D:/") && MediaFiles.IsUrl("rtsp://cam/live"));
            Check("files: a drive path is not a URL", !MediaFiles.IsUrl(@"C:\Videos\a.mp4") && !MediaFiles.IsUrl(@"\\server\share\a.mkv") && !MediaFiles.IsUrl("a://"));
            Check("files: times read in every way people write them",
                MediaFiles.ParseTime("90") == 90000 && MediaFiles.ParseTime("1:30") == 90000 && MediaFiles.ParseTime("1:02:03") == 3723000 &&
                MediaFiles.ParseTime("1h2m3s") == 3723000 && MediaFiles.ParseTime("2m") == 120000 && MediaFiles.ParseTime("45s") == 45000 &&
                MediaFiles.ParseTime("1:30.5") == 90500 && MediaFiles.ParseTime("x") == -1 && MediaFiles.ParseTime("1:2:3:4") == -1 && MediaFiles.ParseTime("") == -1,
                MediaFiles.ParseTime("1h2m3s") + " " + MediaFiles.ParseTime("1:30.5"));
            Check("files: times written as m:ss or h:mm:ss", MediaFiles.Time(65000) == "1:05" && MediaFiles.Time(3723000) == "1:02:03" && MediaFiles.Time(-1) == "--:--" && MediaFiles.Time(0) == "0:00");
            Check("files: names of discs and web addresses", MediaFiles.DisplayName("dvd:///D:/") == "DVD (D:)" && MediaFiles.DisplayName("https://example.com/show/ep%201.mp4") == "ep 1.mp4"
                && MediaFiles.DisplayName(@"C:\a\Film.Name.mkv") == "Film.Name", MediaFiles.DisplayName("dvd:///D:/") + " | " + MediaFiles.DisplayName("https://example.com/show/ep%201.mp4"));

            string d = Dir("folder");
            foreach (string f in new[] { "ep10.mp4", "ep2.mp4", "ep1.mkv", "notes.txt", "cover.jpg", "song.flac" }) File.WriteAllText(Path.Combine(d, f), "x");
            Directory.CreateDirectory(Path.Combine(d, "Season 2"));
            File.WriteAllText(Path.Combine(d, "Season 2", "s2e1.avi"), "x");
            File.WriteAllText(Path.Combine(d, "list.m3u8"), "x");
            var hidden = Path.Combine(d, "hidden.mp4"); File.WriteAllText(hidden, "x"); File.SetAttributes(hidden, FileAttributes.Hidden);
            var got = MediaFiles.ExpandFolder(d).Select(Path.GetFileName).ToList();
            Check("files: a folder becomes its media in natural order, then its subfolders (no hidden files)",
                string.Join(",", got) == "ep1.mkv,ep2.mp4,ep10.mp4,song.flac,s2e1.avi", string.Join(",", got));
            Check("files: playlists in a folder only when asked", MediaFiles.ExpandFolder(d, true).Any(f => f.EndsWith("list.m3u8")) && !MediaFiles.ExpandFolder(d).Any(f => f.EndsWith(".m3u8")));

            string sd = Dir("subs");
            foreach (string f in new[] { "Film.mkv", "Film.srt", "Film.en.srt", "Film.fr.forced.ass", "Other.srt", "Film.txt", "Film2.srt" }) File.WriteAllText(Path.Combine(sd, f), "x");
            Directory.CreateDirectory(Path.Combine(sd, "Subs"));
            File.WriteAllText(Path.Combine(sd, "Subs", "Film.de.srt"), "x");
            Directory.CreateDirectory(Path.Combine(sd, "Subs", "Film"));
            File.WriteAllText(Path.Combine(sd, "Subs", "Film", "2_English.srt"), "x");
            var subs = MediaFiles.FindSubtitles(Path.Combine(sd, "Film.mkv")).Select(s => Path.GetRelativePath(sd, s)).ToList();
            Check("files: subtitles beside a video, best match first (not other films', not .txt)",
                subs.Count == 5 && subs[0] == "Film.srt" && subs.Take(3).Contains("Film.en.srt") && subs.Take(3).Contains("Film.fr.forced.ass")
                && subs.Contains(Path.Combine("Subs", "Film.de.srt")) && subs.Contains(Path.Combine("Subs", "Film", "2_English.srt")) && !subs.Contains("Other.srt") && !subs.Contains("Film2.srt"),
                string.Join(", ", subs));
            Check("files: a path with # and spaces becomes a proper address", MediaFiles.ToMrl(@"C:\My Videos\Part #2.mp4") == "file:///C:/My%20Videos/Part%20%232.mp4", MediaFiles.ToMrl(@"C:\My Videos\Part #2.mp4"));
        }

        static void SettingsChecks()
        {
            string d = Dir("settings");
            string f = Path.Combine(d, "settings.json");
            var s = Settings.Load(f);
            Check("settings: defaults when there is no file", s.Volume == 80 && s.Recent.Count == 0 && s.Resume && s.Loop == 0 && !s.Blocked && s.LoadMessage == "");
            s.Volume = 135; s.AddRecent(@"C:\a.mp4"); s.AddRecent(@"C:\b.mp4"); s.AddRecent(@"C:\A.MP4");
            s.Remember(@"C:\long.mkv", 600000, 3600000);
            s.Remember(@"C:\short.mp3", 60000, 120000);
            s.Remember(@"C:\nearEnd.mkv", 3595000, 3600000);
            s.Eq.On = true; s.Eq.Bands[3] = 4.5f; s.YtRecent.Add("cats");
            s.Extra["fromTheFuture"] = new JsonObject { ["x"] = 1 };
            Check("settings: recent files newest first, no doubles", string.Join(",", s.Recent) == @"C:\A.MP4,C:\b.mp4", string.Join(",", s.Recent));
            Check("settings: resume points only for long files, not near the start or the end",
                s.ResumeAt(@"C:\LONG.mkv") == 600000 && s.ResumeAt(@"C:\short.mp3") == 0 && s.ResumeAt(@"C:\nearEnd.mkv") == 0);
            Check("settings: saved", s.Save() && File.Exists(f));
            var t = Settings.Load(f);
            Check("settings: everything read back", t.Volume == 135 && t.Recent.Count == 2 && t.ResumeAt(@"C:\long.mkv") == 600000 && t.Eq.On && Math.Abs(t.Eq.Bands[3] - 4.5f) < 0.01 && t.YtRecent.SequenceEqual(new[] { "cats" }));
            Check("settings: fields from a newer version are kept", t.Extra["fromTheFuture"] is JsonObject jo && (int)jo["x"] == 1);
            t.Volume = 50; t.Save(); t.Volume = 60; t.Save();
            Check("settings: older versions kept as backups", File.Exists(f + ".bak1") && File.Exists(f + ".bak2"));

            // an unreadable file: set aside, a backup used, the person told
            File.WriteAllText(f, "{ this is not json");
            var u = Settings.Load(f);
            Check("settings: a damaged file is set aside and the newest backup used", u.Volume == 50 && u.LoadMessage.Length > 0 && Directory.GetFiles(d, "settings.json.unreadable-*").Length == 1, u.LoadMessage);
            // nothing readable at all: blocked, never saved over
            string g = Path.Combine(Dir("settings2"), "settings.json");
            File.WriteAllText(g, "[1,2");
            var b = Settings.Load(g);
            Check("settings: when nothing can be read, defaults are used and the file is never saved over", b.Blocked && !b.Save() && File.ReadAllText(g) == "[1,2" && b.LoadMessage.Contains("won't save"));
            // odd values
            string h = Path.Combine(Dir("settings3"), "settings.json");
            File.WriteAllText(h, "{\"volume\": 999, \"loop\": \"2\", \"recent\": \"C:\\\\one.mp4\", \"onTop\": \"sometimes\", \"window\": {\"width\": 10}}");
            var o = Settings.Load(h);
            Check("settings: odd values are brought into range", o.Volume == 200 && o.Loop == 2 && o.Recent.SequenceEqual(new[] { @"C:\one.mp4" }) && o.OnTop == "never" && o.WinWidth == 480,
                $"vol {o.Volume} loop {o.Loop} recent {string.Join(",", o.Recent)} onTop {o.OnTop} width {o.WinWidth}");

            // App Hub's Media Player settings (its own format) come across once
            string old = Path.Combine(Dir("oldsettings"), "settings.json");
            string oldText = "\uFEFF{\r\n    \"Volume\":  0.5,\r\n    \"Recent\":  [ \"C:\\\\v\\\\newest.mkv\", \"C:\\\\v\\\\older.mp4\" ],\r\n    \"SubSize\":  39,\r\n    \"ShowPlaylist\":  true,\r\n" +
                             "    \"LastFolder\":  \"C:\\\\v\",\r\n    \"YtRecent\":  [ \"lofi\" ],\r\n    \"Positions\":  { \"C:\\\\v\\\\newest.mkv\": 754.3 },\r\n    \"PositionTimes\":  { \"C:\\\\v\\\\newest.mkv\": 639000000000000000 }\r\n}";
            File.WriteAllText(old, oldText, new UTF8Encoding(false));
            var n = Settings.Load(Path.Combine(Dir("newsettings"), "settings.json"));
            bool any = OldSettings.ImportInto(n, old);
            Check("settings: App Hub's Media Player settings are brought across",
                any && n.Volume == 50 && n.Recent.SequenceEqual(new[] { @"C:\v\newest.mkv", @"C:\v\older.mp4" }) && n.SubtitleScale == 150 && n.ShowPlaylist && n.LastFolder == @"C:\v"
                && n.YtRecent.SequenceEqual(new[] { "lofi" }) && n.ResumeAt(@"C:\v\newest.mkv") == 754300,
                $"vol {n.Volume} recent {string.Join(",", n.Recent)} subs {n.SubtitleScale} resume {n.ResumeAt(@"C:\v\newest.mkv")}");
            Check("settings: App Hub's file is left exactly as it was", File.ReadAllBytes(old).SequenceEqual(new UTF8Encoding(false).GetBytes(oldText)) && Directory.GetFiles(Path.GetDirectoryName(old)).Length == 1);
            Check("settings: no App Hub settings, nothing brought across", !OldSettings.ImportInto(n, Path.Combine(Temp, "nothing", "settings.json")));
        }

        static void PlaylistChecks()
        {
            var p = new Playlist(7);
            p.Add(new[] { "a.mp4", "b.mp4", "c.mp4", "d.mp4" });
            p.Select(0);
            string Walk(int n, bool user = false) { var r = new List<string>(); for (int i = 0; i < n; i++) r.Add(p.Next(user)?.Path ?? "-"); return string.Join(",", r); }
            Check("playlist: in order, then stops at the end (loop off)", Walk(4) == "b.mp4,c.mp4,d.mp4,-");
            p.Select(0); p.Loop = LoopMode.All;
            Check("playlist: loop all starts again", Walk(5) == "b.mp4,c.mp4,d.mp4,a.mp4,b.mp4");
            p.Loop = LoopMode.One;
            Check("playlist: loop one repeats, but Next still moves on", Walk(2) == "b.mp4,b.mp4" && p.Next(true).Path == "c.mp4");
            p.Loop = LoopMode.Off; p.Select(2);
            Check("playlist: previous", p.Previous().Path == "b.mp4" && p.Previous().Path == "a.mp4" && p.Previous().Path == "a.mp4");
            Check("playlist: the playing item is marked", p.Items.Count(i => i.IsPlaying) == 1 && p.Items[0].IsPlaying);

            var q = new Playlist(11);
            q.Add(Enumerable.Range(1, 20).Select(i => "t" + i + ".mp3"));
            q.Select(4); q.Shuffle = true;
            var seen = new List<string> { q.Current.Path };
            for (int i = 0; i < 19; i++) seen.Add(q.Next(false)?.Path);
            Check("playlist: shuffle plays every item once before it stops", seen.Distinct().Count() == 20 && !seen.Contains(null) && q.Next(false) == null, string.Join(" ", seen));
            Check("playlist: shuffle isn't the plain order", !seen.SequenceEqual(Enumerable.Range(5, 16).Concat(Enumerable.Range(1, 4)).Select(i => "t" + i + ".mp3")));
            q.Loop = LoopMode.All;
            var again = new List<string>(); for (int i = 0; i < 20; i++) again.Add(q.Next(false)?.Path);
            Check("playlist: shuffle with loop all goes round again, each item once", again.Distinct().Count() == 20);

            var r = new Playlist(3);
            r.Add(new[] { "1", "2", "3" }); r.Select(1);
            r.Add(new[] { "next" }, next: true);
            Check("playlist: Play next puts it right after the playing one", string.Join(",", r.Items.Select(i => i.Path)) == "1,2,next,3" && r.Current.Path == "2" && r.Next(true).Path == "next");
            r.Remove(r.Items[0]);
            Check("playlist: removing another item keeps the playing one", r.Current?.Path == "next");
            r.Move(2, 0);
            Check("playlist: moving keeps the playing one", string.Join(",", r.Items.Select(i => i.Path)) == "3,2,next" && r.Current.Path == "next" && r.Items.Count(i => i.IsPlaying) == 1);

            string d = Dir("lists");
            File.WriteAllText(Path.Combine(d, "a.m3u8"), "\uFEFF#EXTM3U\r\n#EXTINF:123,Artist - Song\r\nmusic/song.mp3\r\n# a comment\r\nhttps://radio.example/stream\r\nfile:///C:/Videos/Film%201.mkv\r\n\r\nC:\\abs\\x.mp4\r\n", new UTF8Encoding(false));
            var items = Playlist.ReadFile(Path.Combine(d, "a.m3u8"));
            Check("playlist: .m3u8 with titles, relative paths, web addresses and file:// addresses",
                items.Count == 4 && items[0].Path == Path.Combine(d, "music", "song.mp3") && items[0].Title == "Artist - Song" && items[1].Path == "https://radio.example/stream"
                && items[2].Path == @"C:\Videos\Film 1.mkv" && items[3].Path == @"C:\abs\x.mp4" && items[1].Title == null,
                string.Join(" | ", items.Select(i => i.Path + " / " + i.Title)));
            File.WriteAllBytes(Path.Combine(d, "old.m3u"), Encoding.Latin1.GetBytes("Caf\u00e9.mp3\r\n"));
            Check("playlist: an old ANSI .m3u keeps its accented names", Playlist.ReadFile(Path.Combine(d, "old.m3u"))[0].Path.EndsWith("Caf\u00e9.mp3"));
            File.WriteAllText(Path.Combine(d, "r.pls"), "[playlist]\nFile1=http://a.example/1\nTitle1=One\nFile2=two.ogg\nNumberOfEntries=2\nVersion=2\n");
            var pls = Playlist.ReadFile(Path.Combine(d, "r.pls"));
            Check("playlist: .pls", pls.Count == 2 && pls[0].Title == "One" && pls[1].Path == Path.Combine(d, "two.ogg"));
            var s = new Playlist();
            s.Add(new[] { @"C:\x\one.mp3", "https://b.example/two" });
            s.Items[0].LengthMs = 61000; s.Items[0].Title = "One, the song";
            s.Save(Path.Combine(d, "saved.m3u8"));
            var back = Playlist.ReadFile(Path.Combine(d, "saved.m3u8"));
            Check("playlist: saved .m3u8 reads back the same", back.Count == 2 && back[0].Path == @"C:\x\one.mp3" && back[0].Title == "One, the song" && back[1].Path == "https://b.example/two"
                && File.ReadAllText(Path.Combine(d, "saved.m3u8")).Contains("#EXTINF:61,One, the song"));
        }
    }
}
