namespace SpikeLogic;

public readonly record struct LumaStats(double Mean, double StdDev);

/// <summary>
/// Frame quality metrics computed on a downsampled grayscale copy of the
/// captured BGRA frame. Pure C# — no Windows APIs — so it is unit-testable.
/// </summary>
public static class FrameMetrics
{
    public const double BlackMeanThreshold = 8.0;
    public const double BlackStdDevThreshold = 3.0;

    /// <summary>
    /// Downsample a BGRA frame to a grayscale grid of at most <paramref name="grid"/>
    /// cells on the long edge, using point sampling. Returns row-major luma bytes.
    /// </summary>
    public static byte[] DownsampleGray(ReadOnlySpan<byte> bgra, int width, int height, int strideBytes, int grid, out int outWidth, out int outHeight)
    {
        if (width <= 0 || height <= 0 || strideBytes < width * 4)
            throw new ArgumentException("Invalid frame geometry");
        if (grid <= 0)
            throw new ArgumentOutOfRangeException(nameof(grid));

        int stepX = Math.Max(1, width / grid);
        int stepY = Math.Max(1, height / grid);
        outWidth = Math.Max(1, width / stepX);
        outHeight = Math.Max(1, height / stepY);

        var gray = new byte[outWidth * outHeight];
        for (int y = 0; y < outHeight; y++)
        {
            int srcRow = y * stepY * strideBytes;
            for (int x = 0; x < outWidth; x++)
            {
                int i = srcRow + x * stepX * 4;
                // BT.601 luma from B,G,R bytes.
                int luma = (bgra[i] * 114 + bgra[i + 1] * 587 + bgra[i + 2] * 299) / 1000;
                gray[y * outWidth + x] = (byte)luma;
            }
        }
        return gray;
    }

    /// <summary>
    /// Downsample to a 256-cell grid for sameness detection (finer than black detection).
    /// </summary>
    public static byte[] DownsampleForSameness(ReadOnlySpan<byte> bgra, int width, int height, int strideBytes)
        => DownsampleGray(bgra, width, height, strideBytes, 256, out _, out _);

    public static LumaStats ComputeLuma(ReadOnlySpan<byte> gray)
    {
        if (gray.Length == 0)
            return new LumaStats(0, 0);

        double sum = 0;
        double sumSq = 0;
        foreach (var b in gray)
        {
            sum += b;
            sumSq += (double)b * b;
        }
        double mean = sum / gray.Length;
        double variance = (sumSq / gray.Length) - mean * mean;
        return new LumaStats(mean, Math.Sqrt(Math.Max(0, variance)));
    }

    public static bool IsBlack(LumaStats stats)
        => stats.Mean < BlackMeanThreshold && stats.StdDev < BlackStdDevThreshold;

    /// <summary>FNV-1a 64-bit hash of the downsampled grayscale frame.</summary>
    public static ulong Hash(ReadOnlySpan<byte> gray)
    {
        const ulong offset = 14695981039346656037UL;
        const ulong prime = 1099511628211UL;
        ulong h = offset;
        foreach (var b in gray)
        {
            h ^= b;
            h *= prime;
        }
        return h;
    }

    /// <summary>True when this frame's hash equals the previous frame of the same method.</summary>
    public static bool IsSameAsPrevious(ulong currentHash, ulong? previousHash)
        => previousHash.HasValue && previousHash.Value == currentHash;
}
