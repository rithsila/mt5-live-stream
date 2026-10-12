namespace CaptureSpike;

/// <summary>Minimal --key value parser. Flags without a value get "true".</summary>
internal sealed class Cli
{
    private readonly Dictionary<string, string> _opts = new(StringComparer.OrdinalIgnoreCase);

    public static Cli Parse(string[] args)
    {
        var c = new Cli();
        for (int i = 0; i < args.Length; i++)
        {
            var a = args[i];
            if (!a.StartsWith("--", StringComparison.Ordinal))
                continue;
            var key = a[2..];
            if (i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal))
                c._opts[key] = args[++i];
            else
                c._opts[key] = "true";
        }
        return c;
    }

    public string? Get(string key) => _opts.TryGetValue(key, out var v) ? v : null;

    public string Require(string key) =>
        Get(key) ?? throw new ArgumentException($"missing required option --{key}");

    public int GetInt(string key, int fallback) =>
        int.TryParse(Get(key), out var v) ? v : fallback;

    public string[] GetCsv(string key, string[] fallback) =>
        Get(key) is { } s
            ? s.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            : fallback;
}
