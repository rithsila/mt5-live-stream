namespace SpikeLogic;

public sealed record BenchSetting(int Width, int Height, int Fps, string Preset, int BitrateKbps)
{
    public string Describe() => $"{Width}x{Height}@{Fps}fps {Preset} {BitrateKbps}k";
    public FfmpegJob ToJob(FfmpegTarget target)
        => new(Width, Height, Fps, Preset, BitrateKbps, target);
}

/// <summary>
/// The Phase 0 encoder matrix: {720p, 1080p} x {15, 30 fps} x {veryfast, superfast}.
/// Bitrate: 3000k for 720p, 4500k for 1080p.
/// </summary>
public static class BenchMatrix
{
    public static readonly BenchSetting[] Settings =
    {
        new(1280, 720, 15, "veryfast", 3000),
        new(1280, 720, 15, "superfast", 3000),
        new(1280, 720, 30, "veryfast", 3000),
        new(1280, 720, 30, "superfast", 3000),
        new(1920, 1080, 15, "veryfast", 4500),
        new(1920, 1080, 15, "superfast", 4500),
        new(1920, 1080, 30, "veryfast", 4500),
        new(1920, 1080, 30, "superfast", 4500),
    };

    /// <summary>Bitrate used by stream-test for a given output size.</summary>
    public static int BitrateFor(int height) => height >= 1080 ? 4500 : 3000;
}
