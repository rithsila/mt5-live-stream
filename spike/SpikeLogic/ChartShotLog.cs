namespace SpikeLogic;

/// <summary>
/// Result of parsing the last line of chartshot-log.csv.
/// Format: utc,seq,file,ok,err
/// </summary>
public sealed record ChartShotLogEntry(
    DateTimeOffset Timestamp,
    int Sequence,
    string FileName,
    bool Ok,
    int Error);

public static class ChartShotLog
{
    private const int FreshnessSeconds = 3;

    /// <summary>
    /// Parse the last line of chartshot-log.csv.
    /// Returns null if the log is empty or malformed.
    /// </summary>
    public static ChartShotLogEntry? ParseLastLine(string logText, DateTimeOffset nowUtc)
    {
        if (string.IsNullOrWhiteSpace(logText))
            return null;

        // The EA may be writing the next row. Use only completed CSV rows.
        var end = logText.LastIndexOf('\n');
        if (end < 0) return null;
        var lines = logText[..end].Split('\n', StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length == 0)
            return null;

        var last = lines[^1].Trim();
        if (string.IsNullOrWhiteSpace(last))
            return null;

        var parts = last.Split(',');
        if (parts.Length != 5)
            return null;

        if (!DateTimeOffset.TryParse(parts[0].Trim(), out var stamp))
            return null;

        if (!int.TryParse(parts[1].Trim(), out var seq))
            return null;

        var fileName = parts[2].Trim();
        if (fileName is not ("shot_0.png" or "shot_1.png" or "shot_2.png"))
            return null;

        if (!int.TryParse(parts[3].Trim(), out var okInt))
            return null;
        if (okInt is not (0 or 1)) return null;
        bool ok = okInt == 1;

        if (!int.TryParse(parts[4].Trim(), out var err))
            return null;

        return new ChartShotLogEntry(stamp, seq, fileName, ok, err);
    }

    /// <summary>
    /// Check if a log entry is fresh (written within the last FreshnessSeconds).
    /// </summary>
    public static bool IsFresh(ChartShotLogEntry entry, DateTimeOffset nowUtc)
        => nowUtc >= entry.Timestamp && (nowUtc - entry.Timestamp) < TimeSpan.FromSeconds(FreshnessSeconds);
}
