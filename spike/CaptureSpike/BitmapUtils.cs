using System.Drawing;
using System.Drawing.Imaging;
using SpikeLogic;

namespace CaptureSpike;

/// <summary>System.Drawing helpers: Bitmap ↔ BgraFrame and PNG saving.</summary>
internal static class BitmapUtils
{
    public static BgraFrame BitmapToFrame(Bitmap bmp)
    {
        var rect = new Rectangle(0, 0, bmp.Width, bmp.Height);
        var data = bmp.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            var pixels = new byte[bmp.Width * bmp.Height * 4];
            int stride = bmp.Width * 4;
            for (int y = 0; y < bmp.Height; y++)
                System.Runtime.InteropServices.Marshal.Copy(data.Scan0 + y * data.Stride, pixels, y * stride, stride);
            return new BgraFrame { Pixels = pixels, Width = bmp.Width, Height = bmp.Height };
        }
        finally
        {
            bmp.UnlockBits(data);
        }
    }

    public static Bitmap FrameToBitmap(BgraFrame f)
    {
        var bmp = new Bitmap(f.Width, f.Height, PixelFormat.Format32bppArgb);
        var rect = new Rectangle(0, 0, f.Width, f.Height);
        var data = bmp.LockBits(rect, ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        try
        {
            int stride = f.Width * 4;
            for (int y = 0; y < f.Height; y++)
                System.Runtime.InteropServices.Marshal.Copy(f.Pixels, y * stride, data.Scan0 + y * data.Stride, stride);
        }
        finally
        {
            bmp.UnlockBits(data);
        }
        return bmp;
    }

    public static void SavePng(BgraFrame frame, string path)
    {
        using var bmp = FrameToBitmap(frame);
        bmp.Save(path, ImageFormat.Png);
    }

    /// <summary>New 32bpp bitmap with an HDC taken and released around <paramref name="draw"/>.</summary>
    public static Bitmap IntoNewBitmap(int w, int h, Action<IntPtr> draw)
    {
        var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        try
        {
            using var g = Graphics.FromImage(bmp);
            var hdc = g.GetHdc();
            try { draw(hdc); }
            finally { g.ReleaseHdc(hdc); }
            return bmp;
        }
        catch { bmp.Dispose(); throw; }
    }
}
