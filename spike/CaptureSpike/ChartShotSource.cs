using System.Drawing;
using SpikeLogic;

namespace CaptureSpike;

/// <summary>
/// Reads PNG screenshots written by the SpikeChartShot EA.
/// EA layout: Common\Files\LiveStreamerSpike\terminal-path.txt holds
/// TERMINAL_DATA_PATH; the PNGs live in &lt;data path&gt;\MQL5\Files\LiveStreamerSpike;
/// chartshot-log.csv (in the Common folder) names the newest file.
/// </summary>
internal sealed class ChartShotSource : SourceBase
{
    public override string Name => "chartshot";

    /// <summary>"found" / "not found" — never put paths in the report (they contain the Windows user name).</summary>
    public string Status { get; private set; } = "not found";

    private string? _commonDir;   // Common\Files\LiveStreamerSpike
    private string? _shotsDir;    // <data>\MQL5\Files\LiveStreamerSpike
    private BgraFrame? _cachedFrame;
    private ChartShotLogEntry? _cachedEntry;
    private DateTime _lastLogCheck = DateTime.MinValue;
    private bool _isNewFrameThisCall;

    protected override bool NeedsChartRect => false;

    public ChartShotSource() : base(IntPtr.Zero)
    {
        // Deliberately NOT unsupported when files are missing: the EA may be
        // attached after this source is created, so Grab() re-resolves every
        // tick and reports per-second errors instead of a permanent failure.
        try { Resolve(); } catch { }
        // Do NOT set UnsupportedReason — it must remain null for retry logic.
    }

    private void Resolve()
    {
        var common = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "MetaQuotes", "Terminal", "Common", "Files", "LiveStreamerSpike");
        var pathFile = Path.Combine(common, "terminal-path.txt");
        if (!File.Exists(pathFile))
        {
            Status = "not found";
            return;
        }
        var dataPath = File.ReadAllText(pathFile).Trim();
        if (string.IsNullOrEmpty(dataPath))
        {
            Status = "path empty";
            return;
        }
        _commonDir = common;
        _shotsDir = Path.Combine(dataPath, "MQL5", "Files", "LiveStreamerSpike");
        Status = "found";
    }

    public override CaptureOutcome Capture(Rect chartRect, Rect windowRect)
    {
        _isNewFrameThisCall = false;
        var outcome = base.Capture(chartRect, windowRect);
        SetNewFrame(outcome, outcome.Ok && _isNewFrameThisCall);
        return outcome;
    }

    protected override BgraFrame Grab(Rect chartRect, Rect windowRect)
    {
        // The EA may have been attached after this source was created — retry.
        if (_shotsDir is null)
        {
            try { Resolve(); } catch { }
            if (_shotsDir is null)
                throw new InvalidOperationException("EA files not found (is SpikeChartShot running?)");
        }

        var logPath = Path.Combine(_commonDir!, "chartshot-log.csv");
        var now = DateTime.UtcNow;

        // Read only the tail of the log (last ~1 KB) to avoid reading 5000 lines every call.
        var logTail = ReadSharedTail(logPath, 1024);
        var entry = ChartShotLog.ParseLastLine(logTail, now);

        if (entry is null)
            throw new InvalidOperationException("chartshot log is empty or malformed");

        // Check the ok field: if ChartScreenShot failed, report the error.
        if (!entry.Ok)
            throw new InvalidOperationException($"ChartScreenShot failed (err {entry.Error})");

        // Cache: only decode the PNG if the file name changed.
        _isNewFrameThisCall = entry != _cachedEntry;
        if (_isNewFrameThisCall || _cachedFrame is null)
        {
            var pngPath = Path.Combine(_shotsDir!, entry.FileName);
            var bytes = ReadSharedBytes(pngPath);
            using var ms = new MemoryStream(bytes);
            using var img = Image.FromStream(ms);
            using var bmp = new Bitmap(img);
            _cachedFrame = BitmapUtils.BitmapToFrame(bmp);
            _cachedEntry = entry;
        }

        _lastLogCheck = now;
        return _cachedFrame;
    }

    protected override bool GetFreshness()
    {
        if (_lastLogCheck == DateTime.MinValue)
            return false;
        try
        {
            var entry = ChartShotLog.ParseLastLine(ReadSharedTail(Path.Combine(_commonDir!, "chartshot-log.csv"), 1024), DateTime.UtcNow);
            return entry is { Ok: true } && ChartShotLog.IsFresh(entry, DateTime.UtcNow);
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }

    private static string ReadSharedTail(string path, int maxBytes)
    {
        for (int i = 0; i < 5; i++)
        {
            try
            {
                using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                if (fs.Length <= maxBytes)
                {
                    using var sr = new StreamReader(fs);
                    return sr.ReadToEnd();
                }
                fs.Seek(-maxBytes, SeekOrigin.End);
                using var sr2 = new StreamReader(fs);
                return sr2.ReadToEnd();
            }
            catch (IOException) when (i < 4) { Thread.Sleep(100); }
            catch (UnauthorizedAccessException) when (i < 4) { Thread.Sleep(100); }
        }
        return File.ReadAllText(path);
    }

    private static byte[] ReadSharedBytes(string path)
    {
        for (int i = 0; i < 5; i++)
        {
            try
            {
                using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var ms = new MemoryStream();
                fs.CopyTo(ms);
                return ms.ToArray();
            }
            catch (IOException) when (i < 4) { Thread.Sleep(100); }
            catch (UnauthorizedAccessException) when (i < 4) { Thread.Sleep(100); }
        }
        return File.ReadAllBytes(path);
    }
}
