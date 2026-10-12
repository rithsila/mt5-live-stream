using System.Text.RegularExpressions;

namespace SpikeLogic;

public static partial class Redaction
{
    public const string Redacted = "<redacted>";

    /// <summary>
    /// Chart window titles look like "XAUUSD,H1" or "EURUSD.r,M15".
    /// Everything else (account numbers, server names, plain words) must be hidden.
    /// </summary>
    [GeneratedRegex(@"^[A-Za-z0-9._#+-]+,(?:M(?:1|2|3|4|5|6|10|12|15|20|30)|H(?:1|2|3|4|6|8|12)|D1|W1|MN1)$", RegexOptions.Compiled)]
    private static partial Regex ChartTitlePattern();

    public static bool IsChartTitle(string? title)
        => !string.IsNullOrEmpty(title) && ChartTitlePattern().IsMatch(title);

    /// <summary>
    /// Returns a safe title for the report. The top-level MT5 window title is
    /// never kept (it contains the account number and broker server). Child
    /// titles are kept only when they match the chart pattern.
    /// </summary>
    public static string SafeTitle(string? rawTitle, bool isTopLevel)
    {
        if (isTopLevel)
            return Redacted;
        return IsChartTitle(rawTitle) ? rawTitle! : Redacted;
    }
}
