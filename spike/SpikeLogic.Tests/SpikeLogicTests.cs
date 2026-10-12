using SpikeLogic;

namespace SpikeLogic.Tests;

public class FrameMetricsTests
{
    private static BgraFrame Solid(byte b, byte g, byte r, int w = 64, int h = 64)
    {
        var p = new byte[w * h * 4];
        for (int i = 0; i < p.Length; i += 4) { p[i] = b; p[i + 1] = g; p[i + 2] = r; p[i + 3] = 255; }
        return new BgraFrame { Pixels = p, Width = w, Height = h };
    }

    [Fact]
    public void SolidBlackFrame_IsBlack()
    {
        var f = Solid(0, 0, 0);
        var gray = FrameMetrics.DownsampleGray(f.Pixels, f.Width, f.Height, f.Stride, 64, out _, out _);
        var stats = FrameMetrics.ComputeLuma(gray);
        Assert.Equal(0, stats.Mean);
        Assert.Equal(0, stats.StdDev);
        Assert.True(FrameMetrics.IsBlack(stats));
    }

    [Fact]
    public void BrightFrame_IsNotBlack()
    {
        var f = Solid(200, 200, 200);
        var gray = FrameMetrics.DownsampleGray(f.Pixels, f.Width, f.Height, f.Stride, 64, out _, out _);
        var stats = FrameMetrics.ComputeLuma(gray);
        Assert.False(FrameMetrics.IsBlack(stats));
    }

    [Fact]
    public void MostlyBlackWithOneBrightPixel_IsNotBlack()
    {
        var f = Solid(0, 0, 0);
        // paint a bright block in a corner (bigger than one sample cell)
        for (int y = 0; y < 8; y++)
            for (int x = 0; x < 8; x++)
            {
                int i = (y * 64 + x) * 4;
                f.Pixels[i] = 255; f.Pixels[i + 1] = 255; f.Pixels[i + 2] = 255;
            }
        var gray = FrameMetrics.DownsampleGray(f.Pixels, f.Width, f.Height, f.Stride, 64, out _, out _);
        var stats = FrameMetrics.ComputeLuma(gray);
        Assert.False(FrameMetrics.IsBlack(stats)); // stddev is high
    }

    [Fact]
    public void SameFrames_SameHash_DifferentFrames_DifferentHash()
    {
        var a = Solid(10, 20, 30);
        var b = Solid(10, 20, 30);
        var c = Solid(60, 20, 30);
        ulong ha = FrameMetrics.Hash(FrameMetrics.DownsampleGray(a.Pixels, a.Width, a.Height, a.Stride, 64, out _, out _));
        ulong hb = FrameMetrics.Hash(FrameMetrics.DownsampleGray(b.Pixels, b.Width, b.Height, b.Stride, 64, out _, out _));
        ulong hc = FrameMetrics.Hash(FrameMetrics.DownsampleGray(c.Pixels, c.Width, c.Height, c.Stride, 64, out _, out _));
        Assert.True(FrameMetrics.IsSameAsPrevious(ha, hb));
        Assert.False(FrameMetrics.IsSameAsPrevious(ha, hc));
        Assert.False(FrameMetrics.IsSameAsPrevious(ha, null));
    }
}

public class RedactionTests
{
    [Fact]
    public void ChartTitle_IsKept() => Assert.Equal("XAUUSD,H1", Redaction.SafeTitle("XAUUSD,H1", isTopLevel: false));

    [Fact]
    public void ChartTitleWithSuffix_IsKept() => Assert.Equal("EURUSD.r,M15", Redaction.SafeTitle("EURUSD.r,M15", isTopLevel: false));

    [Fact]
    public void AccountServerTitle_IsRedacted()
        => Assert.Equal(Redaction.Redacted, Redaction.SafeTitle("12345678: Broker-Server", isTopLevel: false));

    [Fact]
    public void TopLevelTitle_IsNeverKept()
        => Assert.Equal(Redaction.Redacted, Redaction.SafeTitle("12345678 - MetaTrader 5", isTopLevel: true));

    [Fact]
    public void ChartPatternOnTopLevel_StillRedacted()
        => Assert.Equal(Redaction.Redacted, Redaction.SafeTitle("XAUUSD,H1", isTopLevel: true));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Toolbox")]
    [InlineData("XAUUSD H1")]       // space, no comma
    [InlineData("xauusd,h1")]       // lowercase timeframe part
    [InlineData("12345678,BROKER")]
    [InlineData("EURUSD,SERVER")]
    public void NonChartTitles_AreRedacted(string? t)
        => Assert.Equal(Redaction.Redacted, Redaction.SafeTitle(t, isTopLevel: false));
}

public class ChartPickerTests
{
    private static WindowNode Node(string cls, int x, int y, int w, int h, bool visible = true)
        => new() { ClassName = cls, Title = "<redacted>", Rect = new Rect(x, y, w, h), Visible = visible };

    [Fact]
    public void ChartOutsideMdiBounds_FailsClosed()
    {
        var top = Node("top", 0, 0, 800, 600);
        var mdi = Node("MDIClient", 0, 50, 800, 500);
        mdi.Children.Add(Node("chart", 0, 0, 800, 600));
        top.Children.Add(mdi);
        Assert.Null(ChartPicker.PickChart(top));
    }

    [Fact]
    public void HiddenMdi_FailsClosed()
    {
        var top = Node("top", 0, 0, 800, 600);
        var mdi = Node("MDIClient", 0, 50, 800, 500, visible: false);
        mdi.Children.Add(Node("chart", 0, 50, 800, 500));
        top.Children.Add(mdi);
        Assert.Null(ChartPicker.PickChart(top));
    }

    [Fact]
    public void PicksLargestMdiChild_AndLargestDescendant()
    {
        var top = Node("MetaQuotes::MetaTrader::5", 0, 0, 1600, 900);
        var mdi = Node("MDIClient", 0, 50, 1600, 800);
        var small = Node("MT5ChartFrame", 0, 50, 400, 300);
        var big = Node("MT5ChartFrame", 400, 50, 1100, 700);
        var canvas = Node("ChartCanvas", 410, 90, 1080, 650);
        big.Children.Add(canvas);
        mdi.Children.Add(small);
        mdi.Children.Add(big);
        top.Children.Add(mdi);

        var pick = ChartPicker.PickChart(top);
        Assert.NotNull(pick);
        Assert.Same(big, pick!.MdiChild);
        Assert.Same(canvas, pick.ChartWindow);
        Assert.Equal(new Rect(410, 90, 1080, 650), pick.ChartScreenRect);
    }

    [Fact]
    public void ChildWithNoDescendants_FallsBackToChildRect()
    {
        var top = Node("top", 0, 0, 800, 600);
        var mdi = Node("MDIClient", 0, 0, 800, 550);
        var child = Node("MT5ChartFrame", 10, 10, 700, 500);
        mdi.Children.Add(child);
        top.Children.Add(mdi);

        var pick = ChartPicker.PickChart(top);
        Assert.NotNull(pick);
        Assert.Null(pick!.ChartWindow);
        Assert.Equal(new Rect(10, 10, 700, 500), pick.ChartScreenRect);
    }

    [Fact]
    public void NoMdiClient_ReturnsNull()
    {
        var top = Node("top", 0, 0, 800, 600);
        top.Children.Add(Node("Toolbar", 0, 0, 800, 40));
        Assert.Null(ChartPicker.PickChart(top));
    }

    [Fact]
    public void IndexSelectsSpecificChild()
    {
        var top = Node("top", 0, 0, 800, 600);
        var mdi = Node("MDIClient", 0, 0, 800, 550);
        var big = Node("c", 0, 0, 700, 500);
        var small = Node("c", 0, 0, 100, 100);
        mdi.Children.Add(big); mdi.Children.Add(small);
        top.Children.Add(mdi);

        var pick = ChartPicker.PickChart(top, childIndex: 1); // index 1 = second largest
        Assert.Same(small, pick!.MdiChild);
    }
}

public class CpuMathTests
{
    [Fact]
    public void ProcessCpuPercent_OneCoreWorthOfWork()
    {
        // 1 s of CPU in 1 s wall on a 4-core machine = 25 % of machine
        Assert.Equal(25.0, CpuMath.ProcessCpuPercent(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1), 4));
    }

    [Fact]
    public void MachineBusyPercent_AllIdle_IsZero()
        => Assert.Equal(0.0, CpuMath.MachineBusyPercent(100, 100, 0));

    [Fact]
    public void MachineBusyPercent_HalfBusy()
        => Assert.Equal(50.0, CpuMath.MachineBusyPercent(50, 60, 40));

    [Fact]
    public void Percentile_Works()
    {
        var s = new List<double> { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 };
        Assert.Equal(10.0, CpuMath.Percentile(s, 100));
        Assert.Equal(9.55, CpuMath.Percentile(s, 95), 2);
    }
}

public class FfmpegArgsTests
{
    [Theory]
    [InlineData(0, 720, 15)]
    [InlineData(1279, 720, 15)]
    [InlineData(1280, 720, 0)]
    public void InvalidVideoSettings_RejectBeforeStartingProcess(int width, int height, int fps)
        => Assert.Throws<ArgumentException>(() => FfmpegArgs.Build(new FfmpegJob(width, height, fps, "veryfast", 3000, new FfmpegTarget.NullTarget())));

    [Fact]
    public void UnencryptedStream_RejectsWithoutPrintingKey()
    {
        var job = new FfmpegJob(1280, 720, 15, "veryfast", 3000,
            new FfmpegTarget.FlvTarget("rtmp://a.rtmp.youtube.com/live2", "private-key"));
        var error = Assert.Throws<ArgumentException>(() => FfmpegArgs.Build(job));
        Assert.DoesNotContain("private-key", error.Message);
    }

    [Fact]
    public void BenchJob_ExactArgs()
    {
        var job = new FfmpegJob(1280, 720, 15, "veryfast", 3000, new FfmpegTarget.NullTarget());
        var args = string.Join(' ', FfmpegArgs.Build(job));
        Assert.Equal(
            "-hide_banner -nostats -progress pipe:2 " +
            "-f rawvideo -pix_fmt bgra -s 1280x720 -framerate 15 -i pipe:0 " +
            "-f lavfi -i anullsrc=channel_layout=stereo:sample_rate=44100 " +
            "-c:v libx264 -preset veryfast -pix_fmt yuv420p -profile:v high " +
            "-g 30 -keyint_min 30 -sc_threshold 0 " +
            "-b:v 3000k -maxrate 3000k -bufsize 6000k " +
            "-c:a aac -b:a 128k -ar 44100 -shortest -f null -",
            args);
    }

    [Fact]
    public void StreamJob_ExactArgs_AndKeyRedacted()
    {
        var job = new FfmpegJob(1920, 1080, 30, "superfast", 4500,
            new FfmpegTarget.FlvTarget("rtmps://a.rtmp.youtube.com/live2", "SECRET-KEY-123"));
        var args = FfmpegArgs.Build(job);
        Assert.Equal("rtmps://a.rtmp.youtube.com/live2/SECRET-KEY-123", args[^1]);
        Assert.Equal("-f", args[^3]);
        Assert.Equal("flv", args[^2]);

        var display = FfmpegArgs.BuildDisplay(job);
        Assert.DoesNotContain("SECRET-KEY-123", display);
        Assert.Contains("rtmps://a.rtmp.youtube.com/live2/***", display);
        Assert.Contains("-g 60 -keyint_min 60", display);
        Assert.Contains("-b:v 4500k -maxrate 4500k -bufsize 9000k", display);
    }

    [Fact]
    public void ProgressParse_ReadsFpsAndSpeed()
    {
        var p = FfmpegProgress.Parse(new[] { "frame=1234", "fps=29.97", "bitrate=3002.5kbits/s", "speed=1.00x", "progress=continue" });
        Assert.Equal(1234, p.Frame);
        Assert.Equal(29.97, p.Fps);
        Assert.Equal(1.0, p.Speed);
        Assert.False(p.Ended);
    }
}

public class BenchMatrixTests
{
    [Fact]
    public void Matrix_Has8Entries()
    {
        Assert.Equal(8, BenchMatrix.Settings.Length);
        Assert.Equal(2, BenchMatrix.Settings.Count(s => s.Width == 1280 && s.BitrateKbps == 3000 && s.Fps == 15));
        Assert.Equal(4, BenchMatrix.Settings.Count(s => s.Height == 1080 && s.BitrateKbps == 4500));
    }
}

public class ImageOpsTests
{
    [Fact]
    public void Crop_ClampsToFrameWithoutAddingOutsidePixels()
    {
        var src = new BgraFrame { Pixels = Enumerable.Range(0, 64).Select(i => (byte)i).ToArray(), Width = 4, Height = 4 };
        var result = ImageOps.Crop(src, new Rect(-1, 1, 3, 8));
        Assert.Equal(2, result.Width);
        Assert.Equal(3, result.Height);
        Assert.Equal(src.Pixels.Skip(16).Take(8), result.Pixels.Take(8));
        Assert.Empty(ImageOps.Crop(src, new Rect(10, 10, 2, 2)).Pixels);
    }

    [Fact]
    public void Downscale_PreservesPixelsAndAspectRatio()
    {
        var src = new BgraFrame { Pixels = Enumerable.Repeat((byte)120, 8 * 4 * 4).ToArray(), Width = 8, Height = 4 };
        var result = ImageOps.Downscale(src, 4);
        Assert.Equal(4, result.Width);
        Assert.Equal(2, result.Height);
        Assert.All(result.Pixels, value => Assert.Equal(120, value));
        Assert.Same(src, ImageOps.Downscale(src, 16));
    }

    [Fact]
    public void Letterbox_InvalidGeometryRejected_EmptySourceBlack()
    {
        var empty = new BgraFrame { Pixels = [], Width = 0, Height = 0 };
        Assert.Throws<ArgumentOutOfRangeException>(() => ImageOps.Letterbox(empty, 0, 2));
        var frame = ImageOps.Letterbox(empty, 2, 2);
        Assert.Equal(new byte[] { 0, 0, 0, 255 }, frame.Pixels.Take(4));
    }

    [Fact]
    public void Letterbox_PadsToOutputSize()
    {
        var src = new BgraFrame { Pixels = new byte[100 * 50 * 4], Width = 100, Height = 50 };
        for (int i = 0; i < src.Pixels.Length; i++) src.Pixels[i] = 200;
        var outF = ImageOps.Letterbox(src, 1280, 720);
        Assert.Equal(1280, outF.Width);
        Assert.Equal(720, outF.Height);
        // top-left corner should be black bar (source is 16:8 -> 1280x640, bars top/bottom)
        Assert.Equal(0, outF.Pixels[0]);
        Assert.Equal(0, outF.Pixels[1]);
        Assert.Equal(0, outF.Pixels[2]);
        // center should be the source color
        int c = (360 * 1280 + 640) * 4;
        Assert.Equal(200, outF.Pixels[c]);
        Assert.Equal(255, outF.Pixels[c + 3]);
    }
}

public class ReportTests
{
    [Fact]
    public void JsonMarkdownAndCsv_PreserveFactsAndEscapeCells()
    {
        var report = new SpikeReport { Label = "test", Command = "capture", FinishedUtc = DateTimeOffset.UtcNow };
        report.Notes.Add("capture only");
        var section = new ReportSection { Title = "summary" };
        section.Facts.Add(new("state", "Disconnected"));
        section.Lines.Add("chart-only");
        var table = new ReportTable { Title = "capture" };
        table.Headers.Add("state");
        table.Rows.Add(["Disconnected|console=False", null]);
        section.Tables.Add(table);
        report.Sections.Add(section);
        report.Timeline.Add(new TimelineRow().Set("state", "a,\"b\"\nc").Set("ms", 1.25).Set("value", 2.5f).Set("ok", true));
        report.Timeline.Add(new TimelineRow().Set("state", null));
        Assert.Contains("Disconnected\\|console=False", report.ToMarkdown());
        Assert.Contains("chart-only", report.ToMarkdown());
        Assert.Contains("Finished (UTC)", report.ToMarkdown());
        Assert.Contains("\"a,\"\"b\"\"\nc\"", report.TimelineToCsv());
        Assert.Contains("1.25,2.5,True", report.TimelineToCsv());
        using var json = System.Text.Json.JsonDocument.Parse(report.ToJson());
        Assert.Equal("test", json.RootElement.GetProperty("label").GetString());

        var dir = Path.Combine(Path.GetTempPath(), "spike-report-test-" + Guid.NewGuid());
        try
        {
            report.WriteTo(dir);
            Assert.True(File.Exists(Path.Combine(dir, "timeline.csv")));
            Assert.Equal(report.ToMarkdown(), File.ReadAllText(Path.Combine(dir, "report.md")));
            Assert.Equal(report.ToJson(), File.ReadAllText(Path.Combine(dir, "report.json")));
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }

    [Fact]
    public void EmptyReport_HasNoFinishedTimeOrTimelineFile()
    {
        var report = new SpikeReport();
        report.Sections.Add(new ReportSection { Title = "empty" });
        Assert.DoesNotContain("Finished (UTC)", report.ToMarkdown());
        var dir = Path.Combine(Path.GetTempPath(), "spike-empty-test-" + Guid.NewGuid());
        try
        {
            report.WriteTo(dir);
            Assert.False(File.Exists(Path.Combine(dir, "timeline.csv")));
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }
}

public class ChartShotLogTests
{
    [Theory]
    [InlineData("../private.png")]
    [InlineData("C:\\private.png")]
    [InlineData("shot_3.png")]
    public void ParseLastLine_UnexpectedFile_ReturnsNull(string file)
        => Assert.Null(ChartShotLog.ParseLastLine($"2026-10-12T10:30:45Z,1,{file},1,0\n", DateTimeOffset.UtcNow));

    [Fact]
    public void ParseLastLine_IgnoresPartialWrite()
    {
        var log = "2026-10-12T10:30:45Z,1,shot_1.png,1,0\n2026-10-12T10:30:46Z,2,";
        Assert.Equal(1, ChartShotLog.ParseLastLine(log, DateTimeOffset.UtcNow)!.Sequence);
    }

    [Fact]
    public void IsFresh_FutureTimestamp_False()
    {
        var now = DateTimeOffset.UtcNow;
        Assert.False(ChartShotLog.IsFresh(new ChartShotLogEntry(now.AddMinutes(5), 1, "shot_1.png", true, 0), now));
    }

    [Fact]
    public void ParseLastLine_Valid_ReturnsEntry()
    {
        var log = "2026-10-12T10:30:45Z,1,shot_1.png,1,0\n";
        var now = DateTimeOffset.Parse("2026-10-12T10:30:46Z");
        var entry = ChartShotLog.ParseLastLine(log, now);
        Assert.NotNull(entry);
        Assert.Equal(1, entry!.Sequence);
        Assert.Equal("shot_1.png", entry.FileName);
        Assert.True(entry.Ok);
        Assert.Equal(0, entry.Error);
    }

    [Fact]
    public void ParseLastLine_OkZero_ReturnsFalse()
    {
        var log = "2026-10-12T10:30:45Z,1,shot_1.png,0,5\n";
        var now = DateTimeOffset.Parse("2026-10-12T10:30:46Z");
        var entry = ChartShotLog.ParseLastLine(log, now);
        Assert.NotNull(entry);
        Assert.False(entry!.Ok);
        Assert.Equal(5, entry.Error);
    }

    [Fact]
    public void ParseLastLine_Empty_ReturnsNull()
    {
        Assert.Null(ChartShotLog.ParseLastLine("", DateTimeOffset.UtcNow));
        Assert.Null(ChartShotLog.ParseLastLine("   ", DateTimeOffset.UtcNow));
    }

    [Fact]
    public void ParseLastLine_Malformed_ReturnsNull()
    {
        Assert.Null(ChartShotLog.ParseLastLine("not,csv", DateTimeOffset.UtcNow));
        Assert.Null(ChartShotLog.ParseLastLine("2026-10-12T10:30:45Z,1,shot_1.png", DateTimeOffset.UtcNow));
    }

    [Fact]
    public void IsFresh_Within3Seconds_True()
    {
        var entry = new ChartShotLogEntry(
            DateTimeOffset.Parse("2026-10-12T10:30:45Z"),
            1, "shot_1.png", true, 0);
        var now = DateTimeOffset.Parse("2026-10-12T10:30:47Z");
        Assert.True(ChartShotLog.IsFresh(entry, now));
    }

    [Fact]
    public void IsFresh_OlderThan3Seconds_False()
    {
        var entry = new ChartShotLogEntry(
            DateTimeOffset.Parse("2026-10-12T10:30:45Z"),
            1, "shot_1.png", true, 0);
        var now = DateTimeOffset.Parse("2026-10-12T10:30:49Z");
        Assert.False(ChartShotLog.IsFresh(entry, now));
    }
}
