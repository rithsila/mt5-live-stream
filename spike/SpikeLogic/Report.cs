using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SpikeLogic;

public sealed class ReportTable
{
    public required string Title { get; set; }
    public List<string> Headers { get; } = new();
    public List<List<string?>> Rows { get; } = new();
}

public sealed class ReportSection
{
    public required string Title { get; set; }
    public List<KeyValuePair<string, string>> Facts { get; } = new();
    public List<ReportTable> Tables { get; } = new();
    /// <summary>Preformatted lines (window tree dump, progress log). Rendered as a code block in Markdown.</summary>
    public List<string> Lines { get; } = new();
}

/// <summary>
/// One row of the per-second timeline. Keys are fixed by the producer;
/// serialized to report.json and timeline.csv.
/// </summary>
public sealed class TimelineRow : Dictionary<string, object?>
{
    public TimelineRow Set(string key, object? value) { this[key] = value; return this; }
}

public sealed class SpikeReport
{
    public string Tool { get; set; } = "CaptureSpike";
    public string Version { get; set; } = "0.2.0";
    public string Label { get; set; } = "";
    public string Command { get; set; } = "";
    public DateTimeOffset StartedUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? FinishedUtc { get; set; }
    public List<string> Notes { get; } = new();
    public List<ReportSection> Sections { get; } = new();
    public List<TimelineRow> Timeline { get; } = new();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);

    public string ToMarkdown()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# CaptureSpike report — {Label}");
        sb.AppendLine();
        sb.AppendLine($"- Tool: {Tool} {Version}");
        sb.AppendLine($"- Command: `{Command}`");
        sb.AppendLine($"- Started (UTC): {StartedUtc:yyyy-MM-dd HH:mm:ss}");
        if (FinishedUtc is { } done)
            sb.AppendLine($"- Finished (UTC): {done:yyyy-MM-dd HH:mm:ss}");
        foreach (var note in Notes)
            sb.AppendLine($"- NOTE: {note}");
        sb.AppendLine();

        foreach (var section in Sections)
        {
            sb.AppendLine($"## {section.Title}");
            sb.AppendLine();
            foreach (var fact in section.Facts)
                sb.AppendLine($"- **{fact.Key}**: {fact.Value}");
            if (section.Facts.Count > 0)
                sb.AppendLine();

            foreach (var table in section.Tables)
            {
                if (!string.IsNullOrEmpty(table.Title))
                    sb.AppendLine($"### {table.Title}");
                sb.AppendLine("| " + string.Join(" | ", table.Headers) + " |");
                sb.AppendLine("|" + string.Join("|", table.Headers.Select(_ => "---")) + "|");
                foreach (var row in table.Rows)
                    sb.AppendLine("| " + string.Join(" | ", row.Select(c => (c ?? "").Replace("|", "\\|").Replace("\n", " "))) + " |");
                sb.AppendLine();
            }

            if (section.Lines.Count > 0)
            {
                sb.AppendLine("```");
                foreach (var line in section.Lines)
                    sb.AppendLine(line);
                sb.AppendLine("```");
                sb.AppendLine();
            }
        }
        return sb.ToString();
    }

    /// <summary>Timeline rendered as CSV (union of keys, first-seen order).</summary>
    public string TimelineToCsv()
    {
        var keys = new List<string>();
        foreach (var row in Timeline)
            foreach (var k in row.Keys)
                if (!keys.Contains(k))
                    keys.Add(k);

        var sb = new StringBuilder();
        sb.AppendLine(string.Join(",", keys));
        foreach (var row in Timeline)
        {
            sb.AppendLine(string.Join(",", keys.Select(k => CsvCell(row.TryGetValue(k, out var v) ? v : null))));
        }
        return sb.ToString();

        static string CsvCell(object? v)
        {
            var s = v switch
            {
                null => "",
                double d => d.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture),
                float f => f.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture),
                _ => v.ToString() ?? "",
            };
            if (s.Contains(',') || s.Contains('"') || s.Contains('\n'))
                s = "\"" + s.Replace("\"", "\"\"") + "\"";
            return s;
        }
    }

    /// <summary>Writes report.json, report.md and (if there are rows) timeline.csv into dirPath.</summary>
    public void WriteTo(string dirPath)
    {
        Directory.CreateDirectory(dirPath);
        File.WriteAllText(Path.Combine(dirPath, "report.json"), ToJson());
        File.WriteAllText(Path.Combine(dirPath, "report.md"), ToMarkdown());
        if (Timeline.Count > 0)
            File.WriteAllText(Path.Combine(dirPath, "timeline.csv"), TimelineToCsv());
    }
}
