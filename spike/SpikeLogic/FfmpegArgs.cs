using System.Text;

namespace SpikeLogic;

public abstract record FfmpegTarget
{
    /// <summary>Encode only, discard output (-f null -). Used by bench.</summary>
    public sealed record NullTarget : FfmpegTarget;

    /// <summary>RTMPS push (-f flv url/key). Used by stream-test.</summary>
    public sealed record FlvTarget(string IngestUrl, string StreamKey) : FfmpegTarget;
}

public sealed record FfmpegJob(
    int Width,
    int Height,
    int Fps,
    string Preset,
    int BitrateKbps,
    FfmpegTarget Target);

public static class FfmpegArgs
{
    /// <summary>
    /// Builds the ffmpeg argument list for a job. The layout is fixed on
    /// purpose — it is the pipeline the production streamer will use.
    /// </summary>
    public static string[] Build(FfmpegJob job)
    {
        if (job.Width is < 2 or > 7680 || job.Height is < 2 or > 4320 ||
            job.Width % 2 != 0 || job.Height % 2 != 0 || job.Fps is < 1 or > 60 || job.BitrateKbps <= 0)
            throw new ArgumentException("Video size must be positive and even; FPS must be 1-60; bitrate must be positive.");
        if (job.Preset is not ("veryfast" or "superfast"))
            throw new ArgumentException("Preset must be veryfast or superfast.");
        if (job.Target is FfmpegTarget.FlvTarget target &&
            (!Uri.TryCreate(target.IngestUrl, UriKind.Absolute, out var url) ||
             url.Scheme != "rtmps" || string.IsNullOrEmpty(url.Host) ||
             url.UserInfo.Length > 0 || url.Query.Length > 0 || url.Fragment.Length > 0 ||
             string.IsNullOrWhiteSpace(target.StreamKey)))
            throw new ArgumentException("Stream target must use RTMPS without URL credentials and a nonempty key.");

        string br = $"{job.BitrateKbps}k";
        string buf = $"{job.BitrateKbps * 2}k";
        int gop = job.Fps * 2;

        var args = new List<string>
        {
            "-hide_banner", "-nostats", "-progress", "pipe:2",
            "-f", "rawvideo", "-pix_fmt", "bgra",
            "-s", $"{job.Width}x{job.Height}",
            "-framerate", job.Fps.ToString(),
            "-i", "pipe:0",
            "-f", "lavfi", "-i", "anullsrc=channel_layout=stereo:sample_rate=44100",
            "-c:v", "libx264", "-preset", job.Preset,
            "-pix_fmt", "yuv420p", "-profile:v", "high",
            "-g", gop.ToString(), "-keyint_min", gop.ToString(),
            "-sc_threshold", "0",
            "-b:v", br, "-maxrate", br, "-bufsize", buf,
            "-c:a", "aac", "-b:a", "128k", "-ar", "44100",
            "-shortest",
        };

        switch (job.Target)
        {
            case FfmpegTarget.NullTarget:
                args.AddRange(new[] { "-f", "null", "-" });
                break;
            case FfmpegTarget.FlvTarget flv:
                args.AddRange(new[] { "-f", "flv", $"{flv.IngestUrl}/{flv.StreamKey}" });
                break;
        }
        return args.ToArray();
    }

    /// <summary>
    /// Command line safe for logs and reports: the stream key is replaced by
    /// "***". The key must never appear in any log output.
    /// </summary>
    public static string BuildDisplay(FfmpegJob job)
    {
        var display = job with
        {
            Target = job.Target is FfmpegTarget.FlvTarget f
                ? f with { StreamKey = "***" }
                : job.Target,
        };
        var sb = new StringBuilder();
        foreach (var a in Build(display))
            sb.Append(a).Append(' ');
        return sb.ToString().TrimEnd();
    }
}

/// <summary>One "-progress" block from ffmpeg stderr.</summary>
public sealed record FfmpegProgress(
    long? Frame,
    double? Fps,
    double? Speed,
    string? Bitrate,
    string? OutTime,
    bool Ended)
{
    /// <summary>Parse key=value progress lines. Input may be a single line or a whole block.</summary>
    public static FfmpegProgress Parse(IEnumerable<string> lines)
    {
        long? frame = null;
        double? fps = null;
        double? speed = null;
        string? bitrate = null;
        string? outTime = null;
        bool ended = false;

        foreach (var line in lines)
        {
            int eq = line.IndexOf('=');
            if (eq <= 0)
                continue;
            var key = line[..eq].Trim();
            var val = line[(eq + 1)..].Trim();
            switch (key)
            {
                case "frame":
                    if (long.TryParse(val, out var f)) frame = f;
                    break;
                case "fps":
                    if (double.TryParse(val, System.Globalization.CultureInfo.InvariantCulture, out var fp)) fps = fp;
                    break;
                case "speed":
                    if (double.TryParse(val.TrimEnd('x'), System.Globalization.CultureInfo.InvariantCulture, out var sp)) speed = sp;
                    break;
                case "bitrate":
                    bitrate = val;
                    break;
                case "out_time":
                    outTime = val;
                    break;
                case "progress":
                    ended = val == "end";
                    break;
            }
        }
        return new FfmpegProgress(frame, fps, speed, bitrate, outTime, ended);
    }
}
