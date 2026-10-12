using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using SpikeLogic;

namespace CaptureSpike;

/// <summary>
/// Reusable System.Drawing compositor for letterboxing BGRA frames.
/// Faster than the managed SpikeLogic.ImageOps.Letterbox for real-time pipelines.
/// </summary>
internal sealed class FrameCompositor : IDisposable
{
    private Bitmap? _outputBmp;
    private Graphics? _graphics;
    private byte[]? _outputBytes;
    private int _lastW, _lastH;

    public FrameCompositor()
    {
        // No initial allocation — allocate on first use.
    }

    /// <summary>
    /// Letterbox the source frame into the output size using a reusable Bitmap.
    /// Returns a byte[] that lives until the next call or disposal.
    /// </summary>
    public byte[] Letterbox(BgraFrame src, int outW, int outH)
    {
        if (outW <= 0 || outH <= 0)
            throw new ArgumentOutOfRangeException(nameof(outW));

        // Reallocate if size changed.
        if (_outputBmp is null || _lastW != outW || _lastH != outH)
        {
            _graphics?.Dispose();
            _outputBmp?.Dispose();
            _outputBmp = new Bitmap(outW, outH, PixelFormat.Format32bppArgb);
            _graphics = Graphics.FromImage(_outputBmp);
            _graphics.CompositingMode = CompositingMode.SourceCopy;
            _graphics.CompositingQuality = CompositingQuality.HighSpeed;
            _graphics.InterpolationMode = InterpolationMode.Bilinear;
            _graphics.PixelOffsetMode = PixelOffsetMode.HighSpeed;
            _graphics.SmoothingMode = SmoothingMode.HighSpeed;
            _outputBytes = new byte[outW * outH * 4];
            _lastW = outW;
            _lastH = outH;
        }

        // Clear to black.
        _graphics!.Clear(Color.Black);

        if (src.Width > 0 && src.Height > 0)
        {
            double scale = Math.Min((double)outW / src.Width, (double)outH / src.Height);
            int fitW = Math.Max(1, (int)Math.Round(src.Width * scale));
            int fitH = Math.Max(1, (int)Math.Round(src.Height * scale));
            int offX = (outW - fitW) / 2;
            int offY = (outH - fitH) / 2;

            // Draw source Bitmap into the letterboxed area.
            var pinned = GCHandle.Alloc(src.Pixels, GCHandleType.Pinned);
            try
            {
                using var srcBmp = new Bitmap(src.Width, src.Height, src.Stride, PixelFormat.Format32bppArgb,
                    pinned.AddrOfPinnedObject());
                _graphics.DrawImage(srcBmp, offX, offY, fitW, fitH);
            }
            finally { pinned.Free(); }
        }

        // Copy to reusable byte[].
        var data = _outputBmp!.LockBits(
            new Rectangle(0, 0, outW, outH),
            ImageLockMode.ReadOnly,
            PixelFormat.Format32bppArgb);
        try
        {
            Marshal.Copy(data.Scan0, _outputBytes!, 0, _outputBytes!.Length);
        }
        finally
        {
            _outputBmp.UnlockBits(data);
        }

        return _outputBytes!;
    }

    public void Dispose()
    {
        _graphics?.Dispose();
        _outputBmp?.Dispose();
        _outputBytes = null;
    }
}
