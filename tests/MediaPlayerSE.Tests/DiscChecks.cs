using System.IO;
using MediaPlayerSauce.Core;

namespace MediaPlayerSE.Tests
{
    static partial class Program
    {
        static void DiscChecks()
        {
            // ------------------------------------------------------------ addresses
            Check("discs: Blu-ray in a drive", Discs.Address(DiscKind.BluRay, @"D:\") == "bluray:///D:/", Discs.Address(DiscKind.BluRay, @"D:\"));
            Check("discs: a drive written as d: or E:/", Discs.Address(DiscKind.BluRay, "d:") == "bluray:///D:/" && Discs.Address(DiscKind.VideoCd, "E:/") == "vcd:///E:/");
            Check("discs: audio CD", Discs.Address(DiscKind.AudioCd, @"D:\") == "cdda:///D:/");
            Check("discs: audio CD ignores a start (its tracks become the playlist)", Discs.Address(DiscKind.AudioCd, @"D:\", 2, 3) == "cdda:///D:/");
            Check("discs: video CD", Discs.Address(DiscKind.VideoCd, @"F:\") == "vcd:///F:/");
            Check("discs: title and chapter", Discs.Address(DiscKind.BluRay, @"D:\", 2, 3) == "bluray:///D:/#2:3", Discs.Address(DiscKind.BluRay, @"D:\", 2, 3));
            Check("discs: title only", Discs.Address(DiscKind.VideoCd, @"D:\", 2) == "vcd:///D:/#2");
            Check("discs: chapter only starts in title 1", Discs.Address(DiscKind.BluRay, @"D:\", 0, 4) == "bluray:///D:/#1:4");
            Check("discs: Blu-ray folder", Discs.Address(DiscKind.BluRay, @"C:\Discs\Holiday") == "bluray:///C:/Discs/Holiday/", Discs.Address(DiscKind.BluRay, @"C:\Discs\Holiday"));
            Check("discs: a BDMV folder means its parent", Discs.Address(DiscKind.BluRay, @"C:\Discs\Holiday\BDMV\") == "bluray:///C:/Discs/Holiday/");
            Check("discs: spaces, # and % in a folder are escaped", Discs.Address(DiscKind.BluRay, @"C:\My Discs\Part #2 100%", 1) == "bluray:///C:/My%20Discs/Part%20%232%20100%25/#1",
                Discs.Address(DiscKind.BluRay, @"C:\My Discs\Part #2 100%", 1));
            Check("discs: Blu-ray image", Discs.Address(DiscKind.BluRay, @"C:\Images\film.iso", 1, 2) == "bluray:///C:/Images/film.iso#1:2", Discs.Address(DiscKind.BluRay, @"C:\Images\film.iso", 1, 2));
            Check("discs: video CD image (.cue)", Discs.Address(DiscKind.VideoCd, @"C:\Images\vcd.cue") == "vcd:///C:/Images/vcd.cue");
            Check("discs: no address for a DVD (it plays as files), an audio CD folder or a video CD folder",
                Discs.Address(DiscKind.Dvd, @"D:\") == null && Discs.Address(DiscKind.AudioCd, @"C:\Music") == null && Discs.Address(DiscKind.VideoCd, @"C:\Vcd") == null && Discs.Address(DiscKind.BluRay, "") == null);
            Check("discs: drives recognised", Discs.IsDrive("D:") && Discs.IsDrive(@"D:\") && Discs.IsDrive("d:/") && !Discs.IsDrive(@"D:\x") && !Discs.IsDrive("1:"));

            // ------------------------------------------------------------ kinds from folder layouts
            string root = Dir("discs");
            string Make(string name, params string[] parts)
            {
                string d = Path.Combine(root, name);
                Directory.CreateDirectory(d);
                foreach (var p in parts)
                {
                    string full = Path.Combine(d, p);
                    if (p.EndsWith("\\")) Directory.CreateDirectory(full);
                    else { Directory.CreateDirectory(Path.GetDirectoryName(full)); File.WriteAllText(full, "x"); }
                }
                return d;
            }
            string dvd = Make("dvd", @"VIDEO_TS\VIDEO_TS.IFO", @"AUDIO_TS\");
            string bd = Make("bd", @"BDMV\index.bdmv", @"CERTIFICATE\");
            string vcd = Make("vcd", @"MPEGAV\AVSEQ01.DAT", @"VCD\INFO.VCD");
            string svcd = Make("svcd", @"MPEG2\AVSEQ01.MPG", @"SVCD\INFO.SVD");
            string cd = Make("cd", "Track01.cda", "Track02.cda");
            string data = Make("data", "notes.txt", @"Photos\a.jpg");
            string blank = Make("blank");
            Check("discs: a DVD folder", Discs.Detect(dvd) == DiscKind.Dvd && Discs.Detect(Path.Combine(dvd, "VIDEO_TS")) == DiscKind.Dvd);
            Check("discs: a Blu-ray folder", Discs.Detect(bd) == DiscKind.BluRay && Discs.Detect(Path.Combine(bd, "BDMV")) == DiscKind.BluRay);
            Check("discs: video CD and super video CD folders", Discs.Detect(vcd) == DiscKind.VideoCd && Discs.Detect(svcd) == DiscKind.VideoCd);
            Check("discs: an audio CD's .cda files", Discs.Detect(cd) == DiscKind.AudioCd);
            Check("discs: a data disc has files but no kind", Discs.Detect(data, true, out bool files) == DiscKind.Unknown && files);
            Check("discs: an empty folder is no kind, an empty drive root an audio CD", Discs.Detect(blank) == DiscKind.Unknown && Discs.Detect(blank, true, out _) == DiscKind.AudioCd);
            Check("discs: a drive that can't be listed is an audio CD", Discs.Detect(Path.Combine(root, "missing"), true, out _) == DiscKind.AudioCd && Discs.Detect(Path.Combine(root, "missing")) == DiscKind.Unknown);

            // ------------------------------------------------------------ DVDs: the main title set
            string film = Make("film", @"AUDIO_TS\");
            string vts = Path.Combine(film, "VIDEO_TS");
            Directory.CreateDirectory(vts);
            void Sized(string name, int kb) { File.WriteAllBytes(Path.Combine(vts, name), new byte[kb * 1024]); }
            Sized("VIDEO_TS.VOB", 900);            // the disc's menu
            Sized("VTS_01_0.VOB", 800);            // title set 1's menu
            Sized("VTS_01_1.VOB", 300);            // a trailer
            Sized("VTS_02_0.VOB", 50);
            Sized("VTS_02_1.VOB", 400);            // the film, in three parts
            Sized("VTS_02_2.VOB", 400);
            Sized("VTS_02_10.VOB", 100);           // part 10 sorts after 2, not after 1
            Sized("VTS_03_1.VOB", 700);            // extras: one big part, still less than the film
            File.WriteAllText(Path.Combine(vts, "VTS_02_0.IFO"), "x");
            Check("discs: the VIDEO_TS folder from the disc folder, the drive-style root or itself",
                Discs.VideoTsFolder(film) == vts && Discs.VideoTsFolder(vts + "\\") == vts && Discs.VideoTsFolder(data) == null && Discs.VideoTsFolder(null) == null);
            var main = Discs.MainTitleSet(vts);
            Check("discs: the main title set is the one with the most bytes (menus left out)", main != null && main.Number == 2 && main.Bytes == 900 * 1024, main?.Number + " " + main?.Bytes);
            Check("discs: its parts in order", main != null && string.Join(",", main.Files.Select(Path.GetFileName)) == "VTS_02_1.VOB,VTS_02_2.VOB,VTS_02_10.VOB",
                main == null ? "" : string.Join(",", main.Files.Select(Path.GetFileName)));
            var sets = Discs.TitleSets(vts);
            Check("discs: every title set, biggest first, no menus", string.Join(",", sets.Select(s => s.Number)) == "2,3,1" && sets.All(s => s.Files.All(f => !f.EndsWith("_0.VOB"))),
                string.Join(",", sets.Select(s => s.Number)));
            Check("discs: a title set asked for", Discs.MainTitleSet(vts, 3)?.Files.Count == 1 && Discs.MainTitleSet(vts, 9) == null);
            Check("discs: no title sets in a folder without them", Discs.MainTitleSet(Path.Combine(dvd, "VIDEO_TS")) == null);

            // ------------------------------------------------------------ disc images
            string IsoWith(string name, string marker)
            {
                string f = Path.Combine(root, name);
                var b = new byte[64 * 1024];
                if (marker != null) System.Text.Encoding.ASCII.GetBytes(marker).CopyTo(b, 40000);
                File.WriteAllBytes(f, b);
                return f;
            }
            Check("discs: a Blu-ray image, a DVD image, an unknown image", Discs.DetectIso(IsoWith("bd.iso", "BDMV")) == DiscKind.BluRay
                && Discs.DetectIso(IsoWith("dvd.iso", "VIDEO_TS")) == DiscKind.Dvd && Discs.DetectIso(IsoWith("x.iso", null)) == DiscKind.Unknown
                && Discs.DetectIso(Path.Combine(root, "missing.iso")) == DiscKind.Unknown);
            Check("discs: image files recognised", Discs.IsImage(@"C:\a.ISO") && Discs.IsImage("b.cue") && !Discs.IsImage("c.mp4"));

            // ------------------------------------------------------------ the PC's drives (whatever is there, never throws)
            List<DiscDrive> drives = null;
            Exception ex = null;
            try { drives = Discs.Drives(); } catch (Exception e) { ex = e; }
            Check("discs: listing the PC's drives never throws", ex == null && drives != null && drives.All(d => d.Describe().StartsWith(d.Letter)), ex?.Message ?? drives.Count + " drive(s)");
            Check("discs: an empty drive says so", new DiscDrive { Letter = "D:", Ready = false }.Describe().Contains("No disc"));
        }
    }
}
