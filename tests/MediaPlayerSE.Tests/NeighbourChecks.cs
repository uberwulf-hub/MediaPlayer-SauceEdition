// Opening one song or video brings the others in its folder, so Next and Previous go through the folder.
using MediaPlayerSauce.Core;
using System.IO;

namespace MediaPlayerSE.Tests
{
    static partial class Program
    {
        static void NeighbourChecks()
        {
            string d = Dir("neighbours");
            foreach (var n in new[] { "track 10.wav", "track 2.wav", "track 1.wav" }) TestMedia.Wav(Path.Combine(d, n), 2);
            TestMedia.Avi(Path.Combine(d, "clip.avi"), 25, 25, 160, 120);
            File.WriteAllText(Path.Combine(d, "cover.jpg"), "x");
            File.WriteAllText(Path.Combine(d, "notes.txt"), "x");
            string hidden = Path.Combine(d, "track 0.wav"); TestMedia.Wav(hidden, 1); File.SetAttributes(hidden, FileAttributes.Hidden);
            Directory.CreateDirectory(Path.Combine(d, "sub")); TestMedia.Wav(Path.Combine(d, "sub", "deeper.wav"), 1);

            var songs = MediaFiles.Neighbours(Path.Combine(d, "track 2.wav")).Select(Path.GetFileName).ToList();
            Check("neighbours: songs beside a song, in Explorer's order, without videos, pictures, hidden files or subfolders",
                string.Join(",", songs) == "track 1.wav,track 2.wav,track 10.wav", string.Join(",", songs));
            var videos = MediaFiles.Neighbours(Path.Combine(d, "clip.avi")).Select(Path.GetFileName).ToList();
            Check("neighbours: videos beside a video", string.Join(",", videos) == "clip.avi", string.Join(",", videos));
            Check("neighbours: a file in a folder that can't be read is just itself", MediaFiles.Neighbours(@"Z:\no\such\song.mp3").Count == 1);

            var w = NewWindow(Path.Combine(d, "s.json"));
            w.OpenPaths(new[] { Path.Combine(d, "track 2.wav") });
            Check("neighbours: opening one song puts the folder's songs in the playlist, starting with that one",
                w.Playlist.Count == 3 && w.Playlist.Current?.Title == "track 2", string.Join(",", w.Playlist.Items.Select(i => i.Title)) + " / " + w.Playlist.Current?.Title);
            WaitUi(() => w.Player.IsPlaying, 4000);
            w.PlayNext(true);
            Check("neighbours: Next goes to the next one in the folder", w.Playlist.Current?.Title == "track 10");
            w.PlayPrevious();
            Check("neighbours: Previous goes back", w.Playlist.Current?.Title == "track 2");
            w.OpenPaths(new[] { Path.Combine(d, "track 1.wav"), Path.Combine(d, "clip.avi") });
            Check("neighbours: opening several files adds just those", w.Playlist.Count == 5);
            w.Settings.FolderNeighbours = false;
            w.Playlist.Clear();
            w.OpenPaths(new[] { Path.Combine(d, "track 1.wav") });
            Check("neighbours: switched off in Preferences, one file is just one file", w.Playlist.Count == 1);
            w.Close();
        }
    }
}
