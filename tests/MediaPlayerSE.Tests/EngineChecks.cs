using System.IO;
using LibVLCSharp.Shared;
using MediaPlayerSauce.Engine;

namespace MediaPlayerSE.Tests
{
    static partial class Program
    {
        static void EngineChecks()
        {
            string d = Dir("media");
            string wav = Path.Combine(d, "tone.wav"), avi = Path.Combine(d, "count #1.avi"), srt = Path.Combine(d, "count #1.srt");
            TestMedia.Wav(wav, 4.0);
            TestMedia.Avi(avi, 50, 10, 320, 240);   // 5 seconds
            TestMedia.Srt(srt, (0.5, 2.0, "Hello there"), (2.5, 4.5, "Second line"));

            // After an update the start-up makes the cache on a background thread (behind a "getting ready" window),
            // before any engine is open: libVLC loads its plugin list once per process, so this comes first.
            string plugins = Path.Combine(AppContext.BaseDirectory, "libvlc", "win-x64", "plugins");
            Player.InitCore();
            try { File.Delete(Path.Combine(plugins, "plugins.dat")); } catch (Exception) { }
            bool built = false;
            var builder = new Thread(() => built = Player.BuildPluginCache());
            builder.Start();
            bool finished = builder.Join(TimeSpan.FromMinutes(3));
            Check("engine: the plugin cache can be made off the window's thread (the first start after an update)",
                finished && built && File.Exists(Path.Combine(plugins, "plugins.dat")) && !Player.PluginCacheStale(plugins),
                $"finished {finished}, built {built}, file {File.Exists(Path.Combine(plugins, "plugins.dat"))}");

            using var p = new Player(new EngineOptions { Headless = true }, a => Posted.Enqueue(a));
            Check("engine: libVLC starts", p.Lib != null && p.Lib.Version.StartsWith("3."), p.Lib?.Version);
            Check("engine: the first start writes libVLC's plugin cache, so later starts don't open every plugin",
                File.Exists(Path.Combine(plugins, "plugins.dat")) && !Player.PluginCacheStale(plugins));

            string pc = Dir("plugin-cache");
            File.WriteAllText(Path.Combine(pc, "a.dll"), "x");
            bool missing = Player.PluginCacheStale(pc);
            File.WriteAllText(Path.Combine(pc, "plugins.dat"), "cache");
            File.SetLastWriteTimeUtc(Path.Combine(pc, "a.dll"), DateTime.UtcNow.AddMinutes(-5));
            bool fresh = !Player.PluginCacheStale(pc);
            File.SetLastWriteTimeUtc(Path.Combine(pc, "a.dll"), DateTime.UtcNow.AddMinutes(5));
            Check("engine: the plugin cache is made again when missing or older than the plugins (an update)",
                missing && fresh && Player.PluginCacheStale(pc) && !Player.PluginCacheStale(Path.Combine(pc, "nope")));
            bool playing = false, ended = false; string failedWith = null; long length = -1;
            p.Playing += () => playing = true;
            p.EndReached += () => ended = true;
            p.Failed += m => failedWith = m;
            p.LengthChanged += l => length = l;

            p.Open(wav);
            Check("engine: a WAV plays", WaitFor(() => playing, 5000), failedWith);
            Check("engine: its length is known (4 s)", WaitFor(() => Math.Abs(p.Length - 4000) < 150, 3000), p.Length + " ms");
            Check("engine: time moves on", WaitFor(() => p.Time > 300, 3000), p.Time + " ms");
            p.Time = 2500;
            Check("engine: seeking", WaitFor(() => p.Time >= 2400, 2000), p.Time + " ms");
            p.Pause();
            Check("engine: pausing", WaitFor(() => p.IsPaused, 2000), p.State.ToString());
            long at = p.Time; Thread.Sleep(300); Pump();
            Check("engine: time stands still while paused", Math.Abs(p.Time - at) < 50, at + " -> " + p.Time);
            p.Play();
            Check("engine: carries on and reaches the end", WaitFor(() => ended, 5000));

            p.Volume = 150; p.Muted = true;
            Check("engine: volume up to 200 and mute are kept", p.Volume == 150 && p.Muted);
            p.Muted = false;

            playing = false; ended = false; failedWith = null;
            p.Open(avi, 1500);
            Check("engine: a video (with # in its name) plays", WaitFor(() => playing, 5000), failedWith);
            Check("engine: it started where asked (1.5 s)", WaitFor(() => p.Time >= 1400, 3000) && p.Time < 3500, p.Time + " ms");
            Check("engine: the video track is found", WaitFor(() => p.VideoTracks().Count >= 1, 3000), string.Join(", ", p.VideoTracks()));
            var vt = p.Tracks().FirstOrDefault(t => t.TrackType == TrackType.Video);
            Check("engine: its size and codec are reported", vt.Data.Video.Width == 320 && vt.Data.Video.Height == 240 && p.CodecName(vt).Length > 0,
                vt.Data.Video.Width + "x" + vt.Data.Video.Height + " " + p.CodecName(vt));
            Check("engine: subtitles beside it are found by libVLC", WaitFor(() => p.SubtitleTracks().Count >= 2, 3000), string.Join(", ", p.SubtitleTracks()));
            string extra = Path.Combine(d, "extra.srt");
            TestMedia.Srt(extra, (1, 3, "Extra"));
            int before = p.SubtitleTracks().Count;
            p.AddSubtitle(extra);
            Check("engine: a subtitle file can be added while playing", WaitFor(() => p.SubtitleTracks().Count > before, 3000), before + " -> " + p.SubtitleTracks().Count);
            p.SubtitleDelayMs = 500; p.AudioDelayMs = -200;
            Check("engine: subtitle and sound delays", p.SubtitleDelayMs == 500 && p.AudioDelayMs == -200, p.SubtitleDelayMs + " / " + p.AudioDelayMs);
            p.Rate = 2f;
            Check("engine: speed", Math.Abs(p.Rate - 2f) < 0.01);
            p.Rate = 1f;

            var presets = Player.EqPresets();
            var freqs = Player.EqFrequencies();
            var rock = Player.EqPresetBands(presets.IndexOf("Rock"), out float pre);
            Check("engine: the equalizer's ten bands and its presets", freqs.Length == 10 && presets.Count >= 18 && presets.Contains("Rock") && rock.Length == 10 && rock.Any(b => b != 0),
                presets.Count + " presets, " + string.Join(" ", freqs.Select(f => f.ToString("0"))));
            p.SetEqualizer(true, pre, rock);
            p.SetEqualizer(false, 0, rock);

            // rebuilding the engine (a start-up option changed) carries on at the same place
            WaitFor(() => p.Time >= 2000, 3000);
            long before2 = p.Time;
            bool rebuilt = false; p.Rebuilt += () => rebuilt = true;
            playing = false;
            p.Rebuild(new EngineOptions { Headless = true, SubtitleScale = 150, Normalize = true });
            Check("engine: rebuilt with new options", rebuilt && p.Options.SubtitleScale == 150 && p.Options.Normalize);
            Check("engine: and carries on where it was", WaitFor(() => playing && p.Time >= before2 - 300, 5000), before2 + " -> " + p.Time);
            Check("engine: the delays are kept", p.SubtitleDelayMs == 500 && p.AudioDelayMs == -200, p.SubtitleDelayMs + " / " + p.AudioDelayMs);

            failedWith = null;
            p.Open(Path.Combine(d, "missing.mp4"));
            Check("engine: a missing file says so", WaitFor(() => failedWith != null, 5000) && failedWith.Contains("missing"), failedWith);
            p.StopNow();
        }
    }
}
