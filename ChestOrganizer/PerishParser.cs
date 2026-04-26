using System.Text.RegularExpressions;

namespace ChestOrganizer;

internal static class PerishParser {
    private static readonly Regex NumericPattern = new(
        @"Fresh for ([\d.]+) (hour|day|year)s?",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex SubHourPattern = new(
        @"less than an hour",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    internal static double? ParseTooltip(string tooltip) {
        if (tooltip == null) return null;

        if (SubHourPattern.IsMatch(tooltip))
            return 0.5;

        var match = NumericPattern.Match(tooltip);
        if (!match.Success) return null;
        if (!double.TryParse(match.Groups[1].Value, out double value)) return null;

        return match.Groups[2].Value.ToLower() switch {
            "hour" => value,
            "day"  => value * 24.0,
            "year" => value * 365.0 * 24.0,
            _      => (double?)null
        };
    }
}
