namespace SpikeLogic;

/// <summary>A raw BGRA frame (stride = Width * 4).</summary>
public sealed class BgraFrame
{
    public required byte[] Pixels { get; init; }
    public required int Width { get; init; }
    public required int Height { get; init; }
    public int Stride => Width * 4;
}

public static class ImageOps
{
    /// <summary>
    /// Letterbox-resize a BGRA frame into an output frame of outW x outH:
    /// black canvas, source scaled (nearest neighbour) to fit, centered.
    /// </summary>
    public static BgraFrame Letterbox(BgraFrame src, int outW, int outH)
    {
        if (outW <= 0 || outH <= 0)
            throw new ArgumentOutOfRangeException(nameof(outW));

        var dst = new byte[outW * outH * 4];
        // Alpha of the black bars = opaque.
        for (int i = 3; i < dst.Length; i += 4)
            dst[i] = 255;

        if (src.Width <= 0 || src.Height <= 0)
            return new BgraFrame { Pixels = dst, Width = outW, Height = outH };

        double scale = Math.Min((double)outW / src.Width, (double)outH / src.Height);
        int fitW = Math.Max(1, (int)Math.Round(src.Width * scale));
        int fitH = Math.Max(1, (int)Math.Round(src.Height * scale));
        int offX = (outW - fitW) / 2;
        int offY = (outH - fitH) / 2;

        for (int y = 0; y < fitH; y++)
        {
            int srcY = Math.Min(src.Height - 1, (int)(y / scale));
            int dstRow = (offY + y) * outW * 4;
            int srcRow = srcY * src.Stride;
            for (int x = 0; x < fitW; x++)
            {
                int srcX = Math.Min(src.Width - 1, (int)(x / scale));
                int s = srcRow + srcX * 4;
                int d = dstRow + (offX + x) * 4;
                dst[d] = src.Pixels[s];
                dst[d + 1] = src.Pixels[s + 1];
                dst[d + 2] = src.Pixels[s + 2];
                dst[d + 3] = 255;
            }
        }
        return new BgraFrame { Pixels = dst, Width = outW, Height = outH };
    }

    /// <summary>Nearest-neighbour downscale so the frame is at most maxWide pixels wide.</summary>
    public static BgraFrame Downscale(BgraFrame src, int maxWide)
    {
        if (src.Width <= maxWide || src.Width <= 0 || src.Height <= 0)
            return src;
        int outW = maxWide;
        int outH = Math.Max(1, (int)Math.Round(src.Height * (double)maxWide / src.Width));
        var dst = new byte[outW * outH * 4];
        for (int y = 0; y < outH; y++)
        {
            int srcY = y * src.Height / outH;
            for (int x = 0; x < outW; x++)
            {
                int srcX = x * src.Width / outW;
                int s = srcY * src.Stride + srcX * 4;
                int d = (y * outW + x) * 4;
                dst[d] = src.Pixels[s];
                dst[d + 1] = src.Pixels[s + 1];
                dst[d + 2] = src.Pixels[s + 2];
                dst[d + 3] = src.Pixels[s + 3];
            }
        }
        return new BgraFrame { Pixels = dst, Width = outW, Height = outH };
    }

    /// <summary>Crop a BGRA frame to a rectangle (clamped to the frame).</summary>
    public static BgraFrame Crop(BgraFrame src, Rect rect)
    {
        var r = rect.Intersect(new Rect(0, 0, src.Width, src.Height));
        if (r.Width <= 0 || r.Height <= 0)
            return new BgraFrame { Pixels = Array.Empty<byte>(), Width = 0, Height = 0 };
        var dst = new byte[r.Width * r.Height * 4];
        for (int y = 0; y < r.Height; y++)
        {
            int s = (r.Y + y) * src.Stride + r.X * 4;
            int d = y * r.Width * 4;
            Array.Copy(src.Pixels, s, dst, d, r.Width * 4);
        }
        return new BgraFrame { Pixels = dst, Width = r.Width, Height = r.Height };
    }
}
