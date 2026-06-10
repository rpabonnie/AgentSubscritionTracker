// SPEC-0003 §4.2 — UsageFormatting display strings (invariant culture, exact outputs).
// Spec-phase stubs: they reference view-model types that do not exist yet, so the
// test project intentionally does not compile until TASK-008 implements SPEC-0003.

using AgentSubscriptionTracker.App.ViewModels;

namespace AgentSubscriptionTracker.Tests.Tray;

public sealed class UsageFormattingTests
{
    private static readonly DateTimeOffset Now = TrayTestData.T0;

    [Theory]
    [InlineData(0, 0, 0, 0, "Resets soon")]                 // exactly zero
    [InlineData(0, 0, 0, 30, "Resets in <1 m")]             // sub-minute
    [InlineData(0, 0, 1, 20, "Resets in 1 m")]              // minutes floor
    [InlineData(0, 0, 47, 20, "Resets in 47 m")]
    [InlineData(0, 0, 59, 59, "Resets in 59 m")]            // just under one hour
    [InlineData(0, 1, 0, 0, "Resets in 1 h 00 m")]          // hour boundary, padded minutes
    [InlineData(0, 2, 5, 0, "Resets in 2 h 05 m")]
    [InlineData(0, 2, 30, 0, "Resets in 2 h 30 m")]
    [InlineData(0, 23, 59, 0, "Resets in 23 h 59 m")]       // just under one day
    [InlineData(1, 0, 0, 0, "Resets in 1 d 0 h")]           // day boundary
    [InlineData(3, 19, 30, 0, "Resets in 3 d 19 h")]        // minutes dropped at day scale
    public void FormatCountdownBoundaries(int days, int hours, int minutes, int seconds, string expected)
    {
        var remaining = new TimeSpan(days, hours, minutes, seconds);

        Assert.Equal(expected, UsageFormatting.FormatCountdown(remaining));
    }

    [Fact]
    public void FormatCountdownClampsNegativeToResetsSoon()
    {
        Assert.Equal("Resets soon", UsageFormatting.FormatCountdown(TimeSpan.FromMinutes(-5)));
    }

    [Fact]
    public void FormatDataAgeNullMeansNoDataYet()
    {
        Assert.Equal("No data yet", UsageFormatting.FormatDataAge(null, Now));
    }

    [Theory]
    [InlineData(0, "Updated just now")]
    [InlineData(5, "Updated just now")]          // < 10 s
    [InlineData(42, "Updated 42 s ago")]
    [InlineData(90, "Updated 1 m ago")]          // minutes floor
    [InlineData(300, "Updated 5 m ago")]
    [InlineData(10_800, "Updated 3 h ago")]
    [InlineData(172_800, "Updated 2 d ago")]
    public void FormatDataAgeBoundaries(int ageSeconds, string expected)
    {
        var last = Now - TimeSpan.FromSeconds(ageSeconds);

        Assert.Equal(expected, UsageFormatting.FormatDataAge(last, Now));
    }

    [Fact]
    public void FormatDataAgeTreatsFutureTimestampAsJustNow()
    {
        // Clock skew between snapshot producer and consumer must not render negative ages.
        var last = Now + TimeSpan.FromSeconds(30);

        Assert.Equal("Updated just now", UsageFormatting.FormatDataAge(last, Now));
    }

    [Fact]
    public void FormatMonthlyResetUsesInvariantMonthDay()
    {
        Assert.Equal("Resets Jul 1", UsageFormatting.FormatMonthlyReset(new DateOnly(2026, 7, 1)));
        Assert.Equal("Resets Dec 31", UsageFormatting.FormatMonthlyReset(new DateOnly(2026, 12, 31)));
    }

    [Theory]
    [InlineData(63.0, "63% used")]
    [InlineData(41.5, "42% used")]   // MidpointRounding.AwayFromZero
    [InlineData(0.0, "0% used")]
    [InlineData(100.0, "100% used")]
    public void FormatPercentUsedRoundsAwayFromZero(double percent, string expected)
    {
        Assert.Equal(expected, UsageFormatting.FormatPercentUsed(percent));
    }
}
