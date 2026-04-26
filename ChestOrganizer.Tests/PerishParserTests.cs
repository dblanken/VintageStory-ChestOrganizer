using Xunit;

namespace ChestOrganizer.Tests;

public class PerishParserTests {
    // ── numeric unit conversion ───────────────────────────────────────────

    [Theory]
    [InlineData("Fresh for 1 hour",    1.0)]
    [InlineData("Fresh for 11 hours",  11.0)]
    [InlineData("Fresh for 1 day",     24.0)]
    [InlineData("Fresh for 3 days",    72.0)]
    [InlineData("Fresh for 1 year",    8760.0)]
    [InlineData("Fresh for 2.5 years", 21900.0)]
    public void ParseTooltip_NumericUnits_ReturnsCorrectHours(string tooltip, double expected) {
        Assert.Equal(expected, PerishParser.ParseTooltip(tooltip));
    }

    // ── sub-hour sentinel ─────────────────────────────────────────────────

    [Fact]
    public void ParseTooltip_LessThanAnHour_ReturnsSentinel() {
        Assert.Equal(0.5, PerishParser.ParseTooltip("Fresh for less than an hour"));
    }

    [Fact]
    public void ParseTooltip_LessThanAnHour_SortsBeforeOneHour() {
        double subHour = PerishParser.ParseTooltip("Fresh for less than an hour")!.Value;
        double oneHour = PerishParser.ParseTooltip("Fresh for 1 hour")!.Value;
        Assert.True(subHour < oneHour);
    }

    // ── ordering invariants ───────────────────────────────────────────────

    [Fact]
    public void ParseTooltip_HoursSortBeforeDays() {
        double hours = PerishParser.ParseTooltip("Fresh for 11 hours")!.Value;
        double days  = PerishParser.ParseTooltip("Fresh for 1 day")!.Value;
        Assert.True(hours < days);
    }

    [Fact]
    public void ParseTooltip_DaysSortBeforeYears() {
        double days  = PerishParser.ParseTooltip("Fresh for 3 days")!.Value;
        double years = PerishParser.ParseTooltip("Fresh for 1 year")!.Value;
        Assert.True(days < years);
    }

    // ── non-perishable returns null ───────────────────────────────────────

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("No perish information here")]
    public void ParseTooltip_NonPerishable_ReturnsNull(string? tooltip) {
        Assert.Null(PerishParser.ParseTooltip(tooltip));
    }

    // ── embedded in VS rich-text HTML ─────────────────────────────────────

    [Fact]
    public void ParseTooltip_IgnoresHtmlTags() {
        string richText = "<font color=\"orange\">Perishable.</font> Fresh for 11 hours";
        Assert.Equal(11.0, PerishParser.ParseTooltip(richText));
    }

    [Fact]
    public void ParseTooltip_SubHour_EmbeddedInRichText() {
        string richText = "<font color=\"orange\">Perishable.</font> Fresh for less than an hour";
        Assert.Equal(0.5, PerishParser.ParseTooltip(richText));
    }
}
