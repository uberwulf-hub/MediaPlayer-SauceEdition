using System.IO;
using System.Security.Cryptography;
using LibVLCSharp.Shared;
using MediaPlayerSauce.Engine;

namespace MediaPlayerSE.Tests
{
    static partial class Program
    {
        static void ConvertChecks()
        {
            string d = Dir("convert");
            string wav = Path.Combine(d, "tone.wav"), avi = Path.Combine(d, "count #2.avi"), longWav = Path.Combine(d, "long tone.wav");
            TestMedia.Wav(wav, 4.0);
            TestMedia.Avi(avi, 125, 25, 320, 240);   // 5 seconds (libavcodec's MPEG-4 encoder mistimes 10 frames a second)
            TestMedia.Wav(longWav, 240.0);
            string wavHash = Hash(wav), aviHash = Hash(avi);

            Check("convert: profiles offered", ConvertProfile.All.Length >= 4 && ConvertProfile.All.All(p => p.Ext.StartsWith(".") && p.Transcode.Length > 0));
            Check("convert: a free name is picked beside an existing file", Converter.FreeName(wav) == Path.Combine(d, "tone (2).wav"), Converter.FreeName(wav));
            var mp4 = ConvertProfile.All.First(p => p.Ext == ".mp4");
            Check("convert: the default copy goes beside the source", Converter.DefaultDestination(avi, mp4) == Path.Combine(d, "count #2.mp4"), Converter.DefaultDestination(avi, mp4));
            bool refused = false;
            using (var c0 = new Converter(a => Posted.Enqueue(a)))
            {
                try { c0.Start(wav, wav, ConvertProfile.All.First(p => !p.Video)); } catch (ArgumentException) { refused = true; }
            }
            Check("convert: the source can't be overwritten", refused);

            using var conv = new Converter(a => Posted.Enqueue(a));
            using var p = new Player(new EngineOptions { Headless = true }, a => Posted.Enqueue(a));

            foreach (var prof in ConvertProfile.All)
            {
                string src = prof.Video ? avi : wav;
                long expect = prof.Video ? 5000 : 4000;
                string outFile = Path.Combine(d, "out " + prof.Ext.TrimStart('.') + prof.Ext);
                string done = null, failed = null; double last = 0;
                Action<string> onDone = f => done = f; Action<string> onFail = m => failed = m; Action<double> onProg = x => last = x;
                conv.Done += onDone; conv.Failed += onFail; conv.Progress += onProg;
                conv.Start(src, outFile, prof);
                bool finished = WaitFor(() => done != null || failed != null, 60000);
                conv.Done -= onDone; conv.Failed -= onFail; conv.Progress -= onProg;
                string tag = "convert: " + prof.Name;
                Check(tag + ": finishes", finished && done == outFile && File.Exists(outFile) && new FileInfo(outFile).Length > 0,
                    failed ?? (File.Exists(outFile) ? new FileInfo(outFile).Length + " bytes" : "no file"));
                Check(tag + ": no partial file left", !Directory.GetFiles(d, "*.partial*").Any(), string.Join(", ", Directory.GetFiles(d, "*.partial*").Select(Path.GetFileName)));
                if (done == null) continue;

                bool playing = false; string pf = null;
                Action onPlay = () => playing = true; Action<string> onPf = m => pf = m;
                p.Playing += onPlay; p.Failed += onPf;
                p.Open(outFile);
                bool plays = WaitFor(() => playing || pf != null, 5000) && pf == null;
                WaitFor(() => p.Length > 0 && (!prof.Video || p.Tracks().Any(t => t.TrackType == TrackType.Video)), 3000);
                long len = p.Length;
                bool video = p.Tracks().Any(t => t.TrackType == TrackType.Video);
                bool audio = p.Tracks().Any(t => t.TrackType == TrackType.Audio);
                Check(tag + ": the copy plays", plays, pf);
                Check(tag + ": with a sensible length", Math.Abs(len - expect) < 800, len + " ms");
                Check(tag + (prof.Video ? ": it has a video track" : ": it has sound and no picture"), prof.Video ? video : audio && !video,
                    string.Join(", ", p.Tracks().Select(t => t.TrackType + " " + p.CodecName(t))));
                p.Playing -= onPlay; p.Failed -= onPf;
                p.StopNow();
                if (prof.Ext == ".flac")
                {
                    var h = new byte[26];
                    using (var fs = File.OpenRead(outFile)) fs.ReadExactly(h, 0, 26);
                    long total = ((long)(h[21] & 0x0F) << 32) | ((long)h[22] << 24) | ((long)h[23] << 16) | ((long)h[24] << 8) | h[25];
                    // libVLC's FLAC encoder doesn't flush its last blocks (about 0.1 s of a 4 s file), so the count is within two blocks
                    Check(tag + ": its header says how long it is", total >= 4 * 44100 - 2 * 4096 && total <= 4 * 44100 && total % 4096 == 0, total + " samples");
                }
                if (prof.Ext == ".mp3")
                {
                    var h = new byte[2];
                    using (var fs = File.OpenRead(outFile)) fs.ReadExactly(h, 0, 2);
                    Check(tag + ": it really is MP3 (MPEG-1 layer III)", h[0] == 0xFF && (h[1] & 0xFE) == 0xFA, h[0].ToString("X2") + h[1].ToString("X2"));
                }
            }

            // cancelling stops and removes the partial file
            {
                var flac = ConvertProfile.All.First(x => x.Ext == ".flac");
                string outFile = Path.Combine(d, "cancelled.flac");
                bool cancelled = false, done = false; string failed = null; double prog = 0;
                Action onCancel = () => cancelled = true; Action<string> onDone = f => done = true, onFail = m => failed = m; Action<double> onProg = x => prog = x;
                conv.Cancelled += onCancel; conv.Done += onDone; conv.Failed += onFail; conv.Progress += onProg;
                conv.Start(longWav, outFile, flac);
                bool partStarted = WaitFor(() => File.Exists(conv.PartFile) && new FileInfo(conv.PartFile).Length > 0, 10000);
                string part = conv.PartFile;
                conv.Cancel();
                bool stopped = WaitFor(() => cancelled, 10000);
                conv.Cancelled -= onCancel; conv.Done -= onDone; conv.Failed -= onFail; conv.Progress -= onProg;
                Check("convert: cancelling stops it", partStarted && stopped && !done && failed == null && !conv.Running, failed);
                Check("convert: and removes the partial file", !File.Exists(part) && !File.Exists(outFile));
            }

            // a missing source fails in plain words
            {
                bool threw = false;
                try { conv.Start(Path.Combine(d, "missing.wav"), Path.Combine(d, "x.flac"), ConvertProfile.All[0]); } catch (ArgumentException) { threw = true; }
                Check("convert: a missing source is refused", threw && !conv.Running);
            }

            Check("convert: the sources are unchanged", Hash(wav) == wavHash && Hash(avi) == aviHash);
            DialogChecks();
        }

        // The Effects, Media information and Convert windows are built and drawn once, off the screen, by the app's
        // own --snapshot (a separate process: its windows are never shown on the screen or activated), so a XAML or
        // style mistake shows up here.
        static void DialogChecks()
        {
            string exe = Path.Combine(AppContext.BaseDirectory, "MediaPlayerSE.exe");
            if (!File.Exists(exe)) { Check("dialogs: the app is beside the tests", false, exe); return; }
            string d = Dir("dialogs");
            foreach (var scene in new[] { "Effects:0", "Effects:1", "Effects:2", "Effects:3", "Info:0", "Info:1", "Info:2", "Convert" })
            {
                string png = Path.Combine(d, scene.Replace(":", "-") + ".png");
                var psi = new System.Diagnostics.ProcessStartInfo(exe) { UseShellExecute = false, RedirectStandardError = true, RedirectStandardOutput = true, CreateNoWindow = true };
                psi.ArgumentList.Add("--snapshot"); psi.ArgumentList.Add(png);
                psi.ArgumentList.Add("--scene"); psi.ArgumentList.Add(scene);
                using var pr = System.Diagnostics.Process.Start(psi);
                var errTask = pr.StandardError.ReadToEndAsync();
                pr.StandardOutput.ReadToEnd();
                bool exited = pr.WaitForExit(60000);
                if (!exited) { try { pr.Kill(true); } catch (Exception) { } }
                string err = errTask.Wait(2000) ? errTask.Result : "";
                Check("dialogs: " + scene + " is built and drawn", exited && pr.ExitCode == 0 && File.Exists(png) && new FileInfo(png).Length > 2000,
                    err.Length > 300 ? err.Substring(0, 300) : err);
            }
        }

        static string Hash(string f)
        {
            using var s = File.OpenRead(f);
            return Convert.ToHexString(SHA256.HashData(s));
        }
    }
}
