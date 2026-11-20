using System.Globalization;
using System.Text.RegularExpressions;

namespace WhyDidItBuild.Core;

public static partial class TextNormalizer
{
    public static string NormalizePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return "";
        }

        return path.Replace('/', '\\').Trim().Trim('"').ToLowerInvariant();
    }

    public static string NormalizeConfiguration(string? configuration)
    {
        if (string.IsNullOrWhiteSpace(configuration))
        {
            return "";
        }

        return configuration.Trim().ToLowerInvariant().Replace(" ", "").Replace("|", "");
    }

    public static string StripVolatileTokens(string text)
    {
        var stripped = TimestampPattern().Replace(text, "#");
        stripped = ClockPattern().Replace(stripped, "#");
        stripped = DurationPattern().Replace(stripped, "#");
        return stripped.Trim();
    }

    public static string FileNameOf(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return "";
        }

        var trimmed = path.Trim().Trim('"');
        var slash = Math.Max(trimmed.LastIndexOf('\\'), trimmed.LastIndexOf('/'));
        return slash >= 0 ? trimmed[(slash + 1)..] : trimmed;
    }

    public static bool ConfigurationsMatch(string? left, string? right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
        {
            return true;
        }

        return string.Equals(NormalizeConfiguration(left), NormalizeConfiguration(right), StringComparison.Ordinal);
    }

    public static bool PlatformsMatch(string? left, string? right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
        {
            return true;
        }

        return string.Equals(NormalizeConfiguration(left), NormalizeConfiguration(right), StringComparison.Ordinal);
    }

    public static string DescribeWhen(DateTimeOffset? value)
    {
        if (value is null)
        {
            return "time unknown";
        }

        return value.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.CurrentCulture);
    }

    [GeneratedRegex(@"\d{4}-\d{2}-\d{2}[ T]\d{2}:\d{2}:\d{2}(?:\.\d+)?", RegexOptions.CultureInvariant)]
    private static partial Regex TimestampPattern();

    [GeneratedRegex(@"\b\d{1,2}/\d{1,2}/\d{2,4}\s+\d{1,2}:\d{2}:\d{2}(?:\.\d+)?\s*(?:AM|PM)?", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ClockPattern();

    [GeneratedRegex(@"\b\d+(?:\.\d+)?\s*ms\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DurationPattern();
}
