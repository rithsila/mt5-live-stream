using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using SpikeLogic;

namespace CaptureSpike;

internal static class Commands
{
    public static readonly string[] AllMethods =
        { "gdi-screen", "gdi-window", "printwindow", "wgc", "chartshot" };

    // ---------- shared helpers ----------

    public static string ReportDir(Cli cli, string label)
    {
        var safe = new string(label.Select(c =>
            char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '-').ToArray());
        var root = cli.Get("report-root") ?? "reports";
        return Path.Combine(root, $"{DateTime.Now:yyyyMMdd-HHmmss}-{safe}");
    }

    public static string ResolveFfmpeg(Cli cli)
    {
        var p = cli.Get("ffmpeg")
            ?? Path.Combine(AppContext.BaseDirectory, "ffmpeg", "ffmpeg.exe");
        if (!File.Exists(p))
            throw new FileNotFoundException(
                $"ffmpeg not found at '{p}'. Run scripts\\Get-FFmpeg.ps1 first, or pass --ffmpeg <path>.");
        return Path.GetFullPath(p);
    }

    /// <summary>Current MT5 pick: largest visible terminal64 main window + its tree + chart pick.</summary>
    private sealed class Mt5Context
    {
        public int Pid;
        public IntPtr Hwnd;
        public int TerminalCount;
        public WindowNode? Tree;
        public ChartPick? Pick;

        public void Refresh(int? chartIndex)
        {
            var wins = WindowTreeBuilder.FindMt5Windows();
            TerminalCount = wins.Count;
            var best = wins
                .Select(w => (w.Pid, w.Hwnd, Rect: RectOf(w.Hwnd)))
                .Where(w => w.Rect.Area > 0)
                .OrderByDescending(w => w.Rect.Area)
                .FirstOrDefault();

            if (best.Hwnd == IntPtr.Zero)
            {
                Pid = 0; Hwnd = IntPtr.Zero; Tree = null; Pick = null;
                return;
            }

            Pid = best.Pid;
            Hwnd = best.Hwnd;
            Tree = WindowTreeBuilder.BuildTree(Hwnd);
            Pick = ChartPicker.PickChart(Tree, chartIndex);
        }

        private static Rect RectOf(IntPtr hwnd)
            => Win32.GetWindowRect(hwnd, out var r) ? r.ToRect() : default;
    }

    private static IFrameSource BuildSource(string method, IntPtr hwnd) => method switch
    {
        "gdi-screen" => new GdiScreenSource(hwnd),
        "gdi-window" => new GdiWindowSource(hwnd),
        "printwindow" => new PrintWindowSource(hwnd),
        "wgc" => new WgcSource(hwnd),
        "chartshot" => new ChartShotSource(),
        _ => throw new ArgumentException($"unknown method '{method}' (choose from {string.Join(", ", AllMethods)})"),
    };

    private static void Countdown(int seconds, string why)
    {
        if (seconds <= 0) return;
        Console.WriteLine($"{why} — starting in {seconds} s. Do the session action now.");
        for (int i = seconds; i > 0; i--)
        {
            Console.Write($"\r  {i,3} s   ");
            Thread.Sleep(1000);
        }
        Console.WriteLine("\r  go!        ");
    }

    private static void AddSystemSections(SpikeReport report)
    {
        var sys = new ReportSection { Title = "System" };
        foreach (var f in SystemInfoProbe.Collect())
            sys.Facts.Add(f);
        report.Sections.Add(sys);
    }

    // ---------- info ----------

    public static int Info(Cli cli, string commandLine)
    {
        var label = cli.Get("label") ?? "info";
        var report = new SpikeReport { Label = label, Command = commandLine };
        AddSystemSections(report);

        var mt5 = new ReportSection { Title = "MT5 windows" };
        var mt5c = new Mt5Context();
        mt5c.Refresh(null);
        mt5.Facts.Add(new("terminal64_processes", mt5c.TerminalCount.ToString()));
        if (mt5c.Tree is not null)
        {
            mt5.Facts.Add(new("picked_window_pid", mt5c.Pid.ToString()));
            mt5.Facts.Add(new("window_rect", mt5c.Tree.Rect.ToString()));
            mt5.Facts.Add(new("chart_pick", mt5c.Pick is null
                ? "no-chart"
                : $"class={mt5c.Pick.ChartWindow?.ClassName ?? "(MDI child rect)"} rect={mt5c.Pick.ChartScreenRect}"));
            mt5.Lines.AddRange(WindowTreeBuilder.Dump(mt5c.Tree, mt5c.Pick));
        }
        else
        {
            mt5.Facts.Add(new("chart_pick", "no MT5 window found"));
        }
        report.Sections.Add(mt5);

        var ff = new ReportSection { Title = "ffmpeg" };
        try { ff.Facts.Add(new("ffmpeg", $"found: {ResolveFfmpeg(cli)}")); }
        catch (Exception ex) { ff.Facts.Add(new("ffmpeg", $"missing: {ex.Message}")); }
        report.Sections.Add(ff);

        report.FinishedUtc = DateTimeOffset.UtcNow;
        var dir = ReportDir(cli, label);
        report.WriteTo(dir);
        Console.WriteLine($"report written to {dir}");
        return 0;
    }

    // ---------- capture ----------

    public static int Capture(Cli cli, string commandLine)
    {
        var label = cli.Get("label") ?? "capture";
        var duration = cli.GetInt("duration", 180);
        var delay = cli.GetInt("delay", 20);
        var methods = cli.GetCsv("methods", AllMethods);
        foreach (var m in methods)
            if (!AllMethods.Contains(m))
                throw new ArgumentException($"unknown method '{m}' (choose from {string.Join(", ", AllMethods)})");
        var chartArg = cli.Get("chart") ?? "auto";
        int? chartIndex = chartArg.Equals("auto", StringComparison.OrdinalIgnoreCase)
            ? null
            : int.Parse(chartArg);

        var report = new SpikeReport { Label = label, Command = commandLine };
        AddSystemSections(report);
        report.Notes.Add($"methods: {string.Join(", ", methods)}; duration {duration} s; chart {(chartIndex?.ToString() ?? "auto")}");

        Countdown(delay, $"capture '{label}'");

        var dir = ReportDir(cli, label);
        var samplesDir = Path.Combine(dir, "samples");
        Directory.CreateDirectory(samplesDir);

        var mt5 = new Mt5Context();
        var sources = new Dictionary<string, IFrameSource>();
        IntPtr sourceHwnd = new(-1);
        var prevHash = new Dictionary<string, ulong>();

        IFrameSource SourceFor(string m)
        {
            if (mt5.Hwnd != sourceHwnd)
            {
                foreach (var old in sources.Values) old.Dispose();
                sources.Clear();
                sourceHwnd = mt5.Hwnd;
            }
            if (!sources.TryGetValue(m, out var src))
            {
                src = BuildSource(m, mt5.Hwnd);
                sources[m] = src;
                if (src.UnsupportedReason is not null)
                    Console.WriteLine($"  [{m}] unsupported: {src.UnsupportedReason}");
            }
            return src;
        }

        var overall = Stopwatch.StartNew();
        for (int t = 0; t < duration; t++)
        {
            var tickStart = overall.Elapsed;
            mt5.Refresh(chartIndex);
            var session = SessionProbe.Current();
            var chartRect = mt5.Pick?.ChartScreenRect ?? default;
            var winRect = mt5.Tree?.Rect ?? default;
            var stateBucket = $"{session.WtsConnectState}|console={session.IsConsoleSession}|rdp={(session.IsRemoteSession ? "y" : "n")}|input={(session.InputDesktopAccessible ? "y" : "n")}";

            var line = $"t={t,4} {session}";
            for (int i = 0; i < methods.Length; i++)
            {
                var m = methods[(i + t) % methods.Length]; // round-robin start
                var outcome = SourceFor(m).Capture(chartRect, winRect);

                var row = new TimelineRow()
                    .Set("t", t)
                    .Set("method", m)
                    .Set("ok", outcome.Ok)
                    .Set("error", outcome.Error)
                    .Set("capture_ms", outcome.CaptureMs)
                    .Set("wts", session.WtsConnectState)
                    .Set("console", session.IsConsoleSession)
                    .Set("remote", session.IsRemoteSession)
                    .Set("input_desktop", session.InputDesktopAccessible)
                    .Set("state", stateBucket)
                    .Set("src_fresh", outcome.SourceFresh);

                if (outcome.Ok && outcome.Frame is { } frame)
                {
                    var gray = FrameMetrics.DownsampleGray(frame.Pixels, frame.Width, frame.Height,
                        frame.Stride, 64, out _, out _);
                    var luma = FrameMetrics.ComputeLuma(gray);
                    var graySameness = FrameMetrics.DownsampleForSameness(frame.Pixels, frame.Width, frame.Height, frame.Stride);
                    var hash = FrameMetrics.Hash(graySameness);
                    prevHash.TryGetValue(m, out var prev);
                    row.Set("width", frame.Width).Set("height", frame.Height)
                        .Set("mean_luma", Math.Round(luma.Mean, 2))
                        .Set("luma_stddev", Math.Round(luma.StdDev, 2))
                        .Set("is_black", FrameMetrics.IsBlack(luma))
                        .Set("is_same", FrameMetrics.IsSameAsPrevious(hash, prev))
                        .Set("new_frame", outcome.NewFrame);
                    prevHash[m] = hash;

                    if (t % 15 == 0)
                    {
                        try
                        {
                            var small = ImageOps.Downscale(frame, 640);
                            BitmapUtils.SavePng(small, Path.Combine(samplesDir, $"{m}-t{t:D4}.png"));
                        }
                        catch (Exception ex) { row.Set("sample_error", ex.Message); }
                    }
                }
                report.Timeline.Add(row);
                line += $" | {m}:{(outcome.Ok ? "ok" : outcome.Error)}";
            }
            Console.WriteLine(line);

            var elapsed = overall.Elapsed - tickStart;
            var wait = TimeSpan.FromSeconds(1) - elapsed;
            if (wait > TimeSpan.Zero)
                Thread.Sleep(wait);
        }

        // ---- which sources ran / were picked ----
        var srcSection = new ReportSection { Title = "Capture sources" };
        foreach (var m in methods)
        {
            if (!sources.TryGetValue(m, out var s))
                continue;
            var status = s.UnsupportedReason is null ? "ok" : $"unsupported: {s.UnsupportedReason}";
            if (s is ChartShotSource cs)
                status += $" | EA files: {cs.Status}";
            srcSection.Facts.Add(new(m, status));
        }
        srcSection.Facts.Add(new("mt5_hwnd_found", mt5.Hwnd != IntPtr.Zero ? "yes" : "no"));
        srcSection.Facts.Add(new("chart_pick", mt5.Pick is null
            ? "no-chart"
            : $"class={mt5.Pick.ChartWindow?.ClassName ?? "(MDI child rect)"} rect={mt5.Pick.ChartScreenRect}"));
        report.Sections.Add(srcSection);

        // ---- summary per method x session state ----
        var sumSection = new ReportSection { Title = "Summary per method and session state" };
        var table = CaptureSummaryTable();
        foreach (var group in report.Timeline.GroupBy(r => (method: r["method"], state: r["state"])).OrderBy(g => g.Key.method?.ToString()).ThenBy(g => g.Key.state?.ToString()))
        {
            var rows = group.ToList();
            double okPct = rows.Count(r => r["ok"] is true) * 100.0 / rows.Count;
            var okRows = rows.Where(r => r["ok"] is true).ToList();
            double blackPct = okRows.Count == 0 ? 0 : okRows.Count(r => r["is_black"] is true) * 100.0 / okRows.Count;
            double samePct = okRows.Count == 0 ? 0 : okRows.Count(r => r["is_same"] is true) * 100.0 / okRows.Count;
            double newFramePct = okRows.Count == 0 ? 0 : okRows.Count(r => r["new_frame"] is true) * 100.0 / okRows.Count;
            double stalePct = okRows.Count == 0 ? 0 : okRows.Count(r => r["src_fresh"] is false) * 100.0 / okRows.Count;
            double avgMs = rows.Average(r => Convert.ToDouble(r["capture_ms"]));
            table.Rows.Add(new() { (string)group.Key.method!, (string)group.Key.state!,
                rows.Count.ToString(), okPct.ToString("0"), blackPct.ToString("0"), samePct.ToString("0"),
                newFramePct.ToString("0"), stalePct.ToString("0"), avgMs.ToString("0.0") });
        }
        sumSection.Tables.Add(table);
        report.Sections.Add(sumSection);

        report.FinishedUtc = DateTimeOffset.UtcNow;
        report.WriteTo(dir);
        Console.WriteLine($"report written to {dir}");
        return report.Timeline.Any(row => row["ok"] is true && row["is_black"] is false && row["src_fresh"] is true) ? 0 : 2;
    }

    // ---------- bench & stream-test share one pump ----------

    private sealed class PumpResult
    {
        public long Written, Dropped, CaptureErrors;
        public double DurationSec;
        public int FfmpegExit;
        public List<string> ProgressLines = new();
        public FfmpegProgress? LastProgress;
        public string? FatalError;
    }

    /// <summary>
    /// Real-time pipeline: capture at target fps -> letterbox to output size ->
    /// raw BGRA into ffmpeg stdin. CPU sampled once per second.
    /// </summary>
    private static PumpResult Pump(
        IFrameSource src, Mt5Context mt5, int? chartIndex,
        FfmpegPipe pipe, CpuMonitor cpu,
        int fps, int outW, int outH, int seconds,
        SpikeReport report, string tag)
    {
        var r = new PumpResult();
        var interval = TimeSpan.FromSeconds(1.0 / fps);
        using var slate = new Bitmap(640, 360);
        using (var graphics = Graphics.FromImage(slate))
        using (var font = new Font("Segoe UI", 18))
        {
            graphics.Clear(Color.Black);
            graphics.DrawString("Reconnecting to MT5...", font, Brushes.White, 30, 150);
        }
        var unavailable = BitmapUtils.BitmapToFrame(slate);
        var sw = Stopwatch.StartNew();
        long tick = 0;
        int lastSec = -1;
        int writtenThisSec = 0, droppedThisSec = 0, errThisSec = 0;
        using var compositor = new FrameCompositor();

        while (sw.Elapsed.TotalSeconds < seconds)
        {
            var target = TimeSpan.FromTicks(interval.Ticks * tick);
            var now = sw.Elapsed;
            if (now < target)
            {
                var wait = target - now;
                if (wait.TotalMilliseconds > 3)
                    Thread.Sleep(wait - TimeSpan.FromMilliseconds(1.5));
                while (sw.Elapsed < target)
                    Thread.SpinWait(50);
            }

            // Late? Skip the frames we can never send.
            now = sw.Elapsed;
            var missed = (long)Math.Floor((now - target) / interval);
            if (missed > 0)
            {
                r.Dropped += missed;
                droppedThisSec += (int)missed;
                tick += missed;
            }

            var chartRect = mt5.Pick?.ChartScreenRect ?? default;
            var winRect = mt5.Tree?.Rect ?? default;
            var o = src.Capture(chartRect, winRect);
            BgraFrame frame;
            if (o.Ok && o.SourceFresh && o.Frame is not null)
            {
                frame = o.Frame;
            }
            else
            {
                r.CaptureErrors++;
                errThisSec++;
                frame = unavailable;
            }

            try
            {
                var letterboxed = compositor.Letterbox(frame, outW, outH);
                pipe.WriteFrame(letterboxed);
                r.Written++;
                writtenThisSec++;
            }
            catch (Exception ex)
            {
                r.FatalError = $"ffmpeg pipe broken: {ex.Message}";
                break;
            }
            tick++;

            if ((int)sw.Elapsed.TotalSeconds > lastSec)
            {
                lastSec = (int)sw.Elapsed.TotalSeconds;
                mt5.Refresh(chartIndex);
                var s = cpu.Sample();
                var p = pipe.Latest;
                report.Timeline.Add(new TimelineRow()
                    .Set("t", lastSec).Set("setting", tag)
                    .Set("sent", writtenThisSec).Set("dropped", droppedThisSec)
                    .Set("cap_err", errThisSec)
                    .Set("cpu_spike", Math.Round(s.SpikePct, 1))
                    .Set("cpu_ffmpeg", Math.Round(s.FfmpegPct, 1))
                    .Set("cpu_mt5", Math.Round(s.Mt5Pct, 1))
                    .Set("cpu_machine", Math.Round(s.MachineBusyPct, 1))
                    .Set("ff_fps", p.Fps).Set("ff_speed", p.Speed)
                    .Set("ff_bitrate", p.Bitrate));
                writtenThisSec = droppedThisSec = errThisSec = 0;
            }
        }

        r.DurationSec = sw.Elapsed.TotalSeconds;
        r.FfmpegExit = pipe.FinishAsync().GetAwaiter().GetResult();
        r.ProgressLines.AddRange(pipe.ProgressLog);
        r.LastProgress = pipe.Latest;
        return r;
    }

    private static ReportTable SummaryTable() => new()
    {
        Title = "bench results",
        Headers = { "setting", "sent fps", "dropped", "cap_err", "cpu spike", "cpu ffmpeg", "cpu mt5",
                    "cpu machine avg", "cpu machine p95", "cpu machine max", "ffmpeg fps", "ffmpeg speed", "exit" },
    };

    private static ReportTable CaptureSummaryTable() => new()
    {
        Title = "capture results",
        Headers = { "method", "session state", "n", "ok %", "black %", "same %", "new-frame %", "stale-src %", "avg ms" },
    };

    public static int Bench(Cli cli, string commandLine)
    {
        var method = cli.Require("method");
        if (!AllMethods.Contains(method))
            throw new ArgumentException($"unknown method '{method}'");
        var seconds = cli.GetInt("seconds", 60);
        var delay = cli.GetInt("delay", 20);
        var label = cli.Get("label") ?? $"bench-{method}";
        var ffmpeg = ResolveFfmpeg(cli);
        var chartArg = cli.Get("chart") ?? "auto";
        int? chartIndex = chartArg.Equals("auto", StringComparison.OrdinalIgnoreCase)
            ? null
            : int.Parse(chartArg);

        var report = new SpikeReport { Label = label, Command = commandLine };
        AddSystemSections(report);
        report.Notes.Add($"method={method}, {BenchMatrix.Settings.Length} settings x {seconds} s");

        Countdown(delay, $"bench '{method}'");

        var dir = ReportDir(cli, label);
        var mt5 = new Mt5Context();
        mt5.Refresh(chartIndex);
        using var src = BuildSource(method, mt5.Hwnd);
        if (src.UnsupportedReason is not null)
            report.Notes.Add($"method unsupported: {src.UnsupportedReason}");
        if (src is ChartShotSource cs1)
            report.Notes.Add($"chartshot EA files: {cs1.Status}");

        var table = SummaryTable();
        bool failed = false;

        foreach (var setting in BenchMatrix.Settings)
        {
            var job = setting.ToJob(new FfmpegTarget.NullTarget());
            var tag = setting.Describe();
            Console.WriteLine($"=== {tag} ({seconds} s) ===");
            var machineSamples = new List<double>();
            var spikeSamples = new List<double>();
            var ffSamples = new List<double>();
            var mt5Samples = new List<double>();
            int rowBase = report.Timeline.Count;

            PumpResult r;
            using (var pipe = new FfmpegPipe(ffmpeg, job))
            {
                var cpu = new CpuMonitor();
                cpu.SetFfmpeg(pipe.Process);
                // Discard one sample to prime the monitor (avoids negative % from previous ffmpeg process).
                cpu.Sample();
                r = Pump(src, mt5, chartIndex, pipe, cpu, setting.Fps,
                    setting.Width, setting.Height, seconds, report, tag);
            }

            foreach (var row in report.Timeline.Skip(rowBase))
            {
                machineSamples.Add(Convert.ToDouble(row["cpu_machine"]));
                spikeSamples.Add(Convert.ToDouble(row["cpu_spike"]));
                ffSamples.Add(Convert.ToDouble(row["cpu_ffmpeg"]));
                mt5Samples.Add(Convert.ToDouble(row["cpu_mt5"]));
            }

            table.Rows.Add(new()
            {
                tag,
                (r.Written / Math.Max(0.1, r.DurationSec)).ToString("0.0"),
                r.Dropped.ToString(),
                r.CaptureErrors.ToString(),
                spikeSamples.Count > 0 ? spikeSamples.Average().ToString("0.0") : "-",
                ffSamples.Count > 0 ? ffSamples.Average().ToString("0.0") : "-",
                mt5Samples.Count > 0 ? mt5Samples.Average().ToString("0.0") : "-",
                machineSamples.Count > 0 ? machineSamples.Average().ToString("0.0") : "-",
                CpuMath.Percentile(machineSamples, 95).ToString("0.0"),
                machineSamples.Count > 0 ? machineSamples.Max().ToString("0.0") : "-",
                r.LastProgress?.Fps?.ToString("0.0") ?? "-",
                r.LastProgress?.Speed?.ToString("0.00") ?? "-",
                r.FfmpegExit.ToString(),
            });
            if (r.FatalError is not null)
                report.Notes.Add($"{tag}: {r.FatalError}");
            failed |= r.FfmpegExit != 0 || r.CaptureErrors > 0 || r.FatalError is not null;
            Thread.Sleep(2000);
        }

        var section = new ReportSection { Title = "Bench" };
        section.Facts.Add(new("ffmpeg_command_shape", FfmpegArgs.BuildDisplay(BenchMatrix.Settings[0].ToJob(new FfmpegTarget.NullTarget()))));
        section.Tables.Add(table);
        report.Sections.Add(section);

        report.FinishedUtc = DateTimeOffset.UtcNow;
        report.WriteTo(dir);
        Console.WriteLine($"report written to {dir}");
        return failed ? 2 : 0;
    }

    public static int StreamTest(Cli cli, string commandLine)
    {
        var method = cli.Require("method");
        if (!AllMethods.Contains(method))
            throw new ArgumentException($"unknown method '{method}'");
        var url = cli.Require("url").TrimEnd('/');
        var keyFile = cli.Require("key-file");
        var minutes = cli.GetInt("minutes", 5);
        var fps = cli.GetInt("fps", 15);
        var preset = cli.Get("preset") ?? "veryfast";
        var label = cli.Get("label") ?? $"stream-{method}";
        var ffmpeg = ResolveFfmpeg(cli);
        var chartArg = cli.Get("chart") ?? "auto";
        int? chartIndex = chartArg.Equals("auto", StringComparison.OrdinalIgnoreCase)
            ? null
            : int.Parse(chartArg);

        var (outW, outH) = ParseSize(cli.Get("size") ?? "1280x720");
        var key = File.ReadAllText(keyFile).Trim();
        if (key.Length == 0)
            throw new ArgumentException("key file is empty");

        var report = new SpikeReport { Label = label, Command = RedactCommand(commandLine, key) };
        AddSystemSections(report);
        report.Notes.Add($"method={method} {outW}x{outH}@{fps} {preset} for {minutes} min");

        Countdown(cli.GetInt("delay", 0), $"stream-test '{method}'");

        var dir = ReportDir(cli, label);
        var mt5 = new Mt5Context();
        mt5.Refresh(chartIndex);
        using var src = BuildSource(method, mt5.Hwnd);
        if (src.UnsupportedReason is not null)
            report.Notes.Add($"method unsupported: {src.UnsupportedReason}");
        if (src is ChartShotSource cs2)
            report.Notes.Add($"chartshot EA files: {cs2.Status}");

        var job = new FfmpegJob(outW, outH, fps, preset, BenchMatrix.BitrateFor(outH),
            new FfmpegTarget.FlvTarget(url, key));

        var section = new ReportSection { Title = "Stream test" };
        PumpResult r;
        using (var pipe = new FfmpegPipe(ffmpeg, job, key))
        {
            var cpu = new CpuMonitor();
            cpu.SetFfmpeg(pipe.Process);
            section.Facts.Add(new("ffmpeg_command", pipe.DisplayCommand));
            r = Pump(src, mt5, chartIndex, pipe, cpu, fps, outW, outH, minutes * 60, report, "stream");
            section.Facts.Add(new("ffmpeg_exit", r.FfmpegExit.ToString()));
            section.Facts.Add(new("sent_fps", (r.Written / Math.Max(0.1, r.DurationSec)).ToString("0.0")));
            section.Facts.Add(new("dropped_frames", r.Dropped.ToString()));
            section.Facts.Add(new("capture_errors", r.CaptureErrors.ToString()));
            if (r.FatalError is not null) section.Facts.Add(new("fatal", r.FatalError));
            section.Lines.Add("--- ffmpeg progress (last blocks) ---");
            section.Lines.AddRange(r.ProgressLines.TakeLast(40));
            section.Lines.Add("--- ffmpeg stderr tail ---");
            section.Lines.AddRange(pipe.StderrTail);
        }
        report.Sections.Add(section);

        report.FinishedUtc = DateTimeOffset.UtcNow;
        report.WriteTo(dir);
        Console.WriteLine($"report written to {dir}");
        return r.FfmpegExit != 0 || r.CaptureErrors > 0 || r.FatalError is not null ? 2 : 0;
    }

    private static (int w, int h) ParseSize(string s)
    {
        var parts = s.Split('x', 'X');
        if (parts.Length == 2 && int.TryParse(parts[0], out var w) && int.TryParse(parts[1], out var h))
            return (w, h);
        throw new ArgumentException($"bad --size '{s}' (use e.g. 1280x720)");
    }

    private static string RedactCommand(string commandLine, string key)
        => key.Length > 0 ? commandLine.Replace(key, "***") : commandLine;
}
