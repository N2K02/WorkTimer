using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

public static class IconBuilder
{
    public static Bitmap RenderMaster(int size)
    {
        Bitmap bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using (Graphics g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.Clear(Color.Transparent);

            float s = (float)size;
            float pad = s * 0.08f;
            float rectSize = s - (pad * 2f);
            float radius = s * 0.22f;

            // Rounded rectangle path for the badge
            using (GraphicsPath badgePath = RoundedRect(new RectangleF(pad, pad, rectSize, rectSize), radius))
            {
                // Modern Fluent blue/cyan gradient badge
                using (LinearGradientBrush brush = new LinearGradientBrush(
                    new PointF(0, pad),
                    new PointF(0, pad + rectSize),
                    Color.FromArgb(255, 37, 99, 235),   // #2563EB Royal Blue
                    Color.FromArgb(255, 15, 23, 42)))   // #0F172A Deep Navy
                {
                    g.FillPath(brush, badgePath);
                }

                // Subtle top highlight border
                using (Pen pen = new Pen(Color.FromArgb(70, 255, 255, 255), s * 0.015f))
                {
                    g.DrawPath(pen, badgePath);
                }
            }

            // Center clock coordinates
            float cx = s / 2f;
            float cy = s / 2f;
            float clockR = s * 0.26f;

            // Clock outer dial ring
            float ringWidth = Math.Max(2f, s * 0.045f);
            using (Pen ringPen = new Pen(Color.FromArgb(240, 255, 255, 255), ringWidth))
            {
                g.DrawEllipse(ringPen, cx - clockR, cy - clockR, clockR * 2f, clockR * 2f);
            }

            // Hour ticks at 12, 3, 6, 9
            using (SolidBrush tickBrush = new SolidBrush(Color.FromArgb(200, 255, 255, 255)))
            {
                float tickR = s * 0.018f;
                float tickDist = clockR * 0.72f;
                g.FillEllipse(tickBrush, cx - tickR, cy - tickDist - tickR, tickR * 2f, tickR * 2f); // 12
                g.FillEllipse(tickBrush, cx + tickDist - tickR, cy - tickR, tickR * 2f, tickR * 2f); // 3
                g.FillEllipse(tickBrush, cx - tickR, cy + tickDist - tickR, tickR * 2f, tickR * 2f); // 6
                g.FillEllipse(tickBrush, cx - tickDist - tickR, cy - tickR, tickR * 2f, tickR * 2f); // 9
            }

            // Clock hands: Minute hand pointing up to ~12:00
            float minHandW = Math.Max(2f, s * 0.04f);
            using (Pen minPen = new Pen(Color.White, minHandW))
            {
                minPen.StartCap = LineCap.Round;
                minPen.EndCap = LineCap.Round;
                g.DrawLine(minPen, cx, cy, cx, cy - (clockR * 0.58f));
            }

            // Hour hand pointing to 9:00 (representing 9-hour work day)
            float hrHandW = Math.Max(2f, s * 0.048f);
            using (Pen hrPen = new Pen(Color.White, hrHandW))
            {
                hrPen.StartCap = LineCap.Round;
                hrPen.EndCap = LineCap.Round;
                g.DrawLine(hrPen, cx, cy, cx - (clockR * 0.42f), cy);
            }

            // Center hub (accent coral dot matching flyout theme)
            float hubR = s * 0.038f;
            using (SolidBrush hubBrush = new SolidBrush(Color.FromArgb(255, 248, 113, 113))) // #F87171 Coral/Orange Accent
            {
                g.FillEllipse(hubBrush, cx - hubR, cy - hubR, hubR * 2f, hubR * 2f);
            }
        }

        return bmp;
    }

    private static GraphicsPath RoundedRect(RectangleF bounds, float radius)
    {
        GraphicsPath path = new GraphicsPath();
        float d = radius * 2f;
        path.AddArc(bounds.X, bounds.Y, d, d, 180, 90);
        path.AddArc(bounds.Right - d, bounds.Y, d, d, 270, 90);
        path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
        path.AddArc(bounds.X, bounds.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    public static void SaveIco(Bitmap[] images, string icoPath)
    {
        using (FileStream stream = new FileStream(icoPath, FileMode.Create, FileAccess.Write))
        using (BinaryWriter writer = new BinaryWriter(stream))
        {
            // ICO Header
            writer.Write((short)0);      // Reserved
            writer.Write((short)1);      // Type 1 = ICO
            writer.Write((short)images.Length); // Image count

            int offset = 6 + (16 * images.Length);
            byte[][] pngBytesList = new byte[images.Length][];

            for (int i = 0; i < images.Length; i++)
            {
                using (MemoryStream ms = new MemoryStream())
                {
                    images[i].Save(ms, ImageFormat.Png);
                    pngBytesList[i] = ms.ToArray();
                }

                int w = images[i].Width >= 256 ? 0 : images[i].Width;
                int h = images[i].Height >= 256 ? 0 : images[i].Height;

                writer.Write((byte)w);
                writer.Write((byte)h);
                writer.Write((byte)0);      // Color count (0 = no palette)
                writer.Write((byte)0);      // Reserved
                writer.Write((short)1);     // Color planes
                writer.Write((short)32);    // Bits per pixel
                writer.Write(pngBytesList[i].Length); // Size in bytes
                writer.Write(offset);       // File offset

                offset += pngBytesList[i].Length;
            }

            for (int i = 0; i < images.Length; i++)
            {
                writer.Write(pngBytesList[i]);
            }
        }
    }
}
