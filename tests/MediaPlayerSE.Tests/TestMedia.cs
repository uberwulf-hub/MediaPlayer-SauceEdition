// Test files made on the spot (nothing downloaded, nothing anyone else made): a WAV of tones, an MJPEG AVI whose
// frames show their own number, and subtitle files.
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace MediaPlayerSE.Tests
{
    static class TestMedia
    {
        /// <summary>A 16-bit stereo WAV: a 440 Hz tone on the left, 660 Hz on the right.</summary>
        public static void Wav(string file, double seconds, int rate = 44100)
        {
            int n = (int)(seconds * rate);
            using var w = new BinaryWriter(File.Create(file));
            int data = n * 4;
            w.Write(Encoding.ASCII.GetBytes("RIFF")); w.Write(36 + data); w.Write(Encoding.ASCII.GetBytes("WAVE"));
            w.Write(Encoding.ASCII.GetBytes("fmt ")); w.Write(16); w.Write((short)1); w.Write((short)2); w.Write(rate); w.Write(rate * 4); w.Write((short)4); w.Write((short)16);
            w.Write(Encoding.ASCII.GetBytes("data")); w.Write(data);
            for (int i = 0; i < n; i++)
            {
                w.Write((short)(Math.Sin(2 * Math.PI * 440 * i / rate) * 8000));
                w.Write((short)(Math.Sin(2 * Math.PI * 660 * i / rate) * 8000));
            }
        }

        /// <summary>An MJPEG AVI (no sound) of frames frames at fps, width x height, each frame showing its number.</summary>
        public static void Avi(string file, int frames, int fps, int width, int height)
        {
            var jpegs = new List<byte[]>();
            for (int f = 0; f < frames; f++) jpegs.Add(Frame(f, width, height));
            using var fs = File.Create(file);
            using var w = new BinaryWriter(fs);
            int maxFrame = jpegs.Max(j => j.Length);
            // RIFF header, filled in at the end
            w.Write(Encoding.ASCII.GetBytes("RIFF")); long riffSize = fs.Position; w.Write(0); w.Write(Encoding.ASCII.GetBytes("AVI "));
            // hdrl
            w.Write(Encoding.ASCII.GetBytes("LIST")); long hdrlSize = fs.Position; w.Write(0); w.Write(Encoding.ASCII.GetBytes("hdrl"));
            w.Write(Encoding.ASCII.GetBytes("avih")); w.Write(56);
            w.Write(1000000 / fps); w.Write(maxFrame * fps); w.Write(0); w.Write(0x10); // flags: has index
            w.Write(frames); w.Write(0); w.Write(1); w.Write(maxFrame); w.Write(width); w.Write(height);
            w.Write(0); w.Write(0); w.Write(0); w.Write(0);
            w.Write(Encoding.ASCII.GetBytes("LIST")); long strlSize = fs.Position; w.Write(0); w.Write(Encoding.ASCII.GetBytes("strl"));
            w.Write(Encoding.ASCII.GetBytes("strh")); w.Write(56);
            w.Write(Encoding.ASCII.GetBytes("vids")); w.Write(Encoding.ASCII.GetBytes("MJPG"));
            w.Write(0); w.Write((short)0); w.Write((short)0); w.Write(0);
            w.Write(1); w.Write(fps); w.Write(0); w.Write(frames); w.Write(maxFrame); w.Write(-1); w.Write(0);
            w.Write((short)0); w.Write((short)0); w.Write((short)width); w.Write((short)height);
            w.Write(Encoding.ASCII.GetBytes("strf")); w.Write(40);
            w.Write(40); w.Write(width); w.Write(height); w.Write((short)1); w.Write((short)24);
            w.Write(Encoding.ASCII.GetBytes("MJPG")); w.Write(width * height * 3); w.Write(0); w.Write(0); w.Write(0); w.Write(0);
            Patch(w, strlSize); Patch(w, hdrlSize);
            // movi
            w.Write(Encoding.ASCII.GetBytes("LIST")); long moviSize = fs.Position; w.Write(0); w.Write(Encoding.ASCII.GetBytes("movi"));
            long moviStart = fs.Position - 4;
            var offsets = new List<(int Off, int Len)>();
            foreach (var j in jpegs)
            {
                offsets.Add(((int)(fs.Position - moviStart), j.Length));
                w.Write(Encoding.ASCII.GetBytes("00dc")); w.Write(j.Length); w.Write(j);
                if ((j.Length & 1) != 0) w.Write((byte)0);
            }
            Patch(w, moviSize);
            // idx1
            w.Write(Encoding.ASCII.GetBytes("idx1")); w.Write(offsets.Count * 16);
            foreach (var o in offsets) { w.Write(Encoding.ASCII.GetBytes("00dc")); w.Write(0x10); w.Write(o.Off); w.Write(o.Len); }
            Patch(w, riffSize);
        }

        static void Patch(BinaryWriter w, long sizeAt)
        {
            long end = w.BaseStream.Position;
            w.BaseStream.Position = sizeAt;
            w.Write((int)(end - sizeAt - 4));
            w.BaseStream.Position = end;
        }

        static byte[] Frame(int n, int width, int height)
        {
            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                var hue = Color.FromRgb((byte)(40 + n * 37 % 180), (byte)(60 + n * 53 % 150), (byte)(90 + n * 29 % 140));
                dc.DrawRectangle(new SolidColorBrush(hue), null, new Rect(0, 0, width, height));
                var ft = new FormattedText(n.ToString(), System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                    new Typeface("Segoe UI"), height * 0.5, Brushes.White, 1.0);
                dc.DrawText(ft, new Point((width - ft.Width) / 2, (height - ft.Height) / 2));
            }
            var bmp = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            bmp.Render(dv);
            var enc = new JpegBitmapEncoder { QualityLevel = 80 };
            enc.Frames.Add(BitmapFrame.Create(bmp));
            using var ms = new MemoryStream();
            enc.Save(ms);
            return ms.ToArray();
        }

        public static void Srt(string file, params (double From, double To, string Text)[] cues)
        {
            var sb = new StringBuilder();
            int i = 1;
            foreach (var c in cues)
            {
                sb.Append(i++).Append("\r\n").Append(Ts(c.From)).Append(" --> ").Append(Ts(c.To)).Append("\r\n").Append(c.Text).Append("\r\n\r\n");
            }
            File.WriteAllText(file, sb.ToString(), new UTF8Encoding(false));
        }
        static string Ts(double s)
        {
            var t = TimeSpan.FromSeconds(s);
            return $"{(int)t.TotalHours:00}:{t.Minutes:00}:{t.Seconds:00},{t.Milliseconds:000}";
        }
    }
}
