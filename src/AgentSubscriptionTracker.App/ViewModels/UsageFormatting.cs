using System.Globalization;

namespace AgentSubscriptionTracker.App.ViewModels;

/// <summary>Pure, culture-invariant display formatting. SPEC-0003 §4.2 (exact strings).</summary>
public static class UsageFormatting
{
    /// <summary>"Resets soon" | "Resets in &lt;1 m" | "Resets in 47 m" | "Resets in 2 h 05 m" | "Resets in 3 d 19 h".</summary>
    public static string FormatCountdown(TimeSpan timeUntilReset)
    {
        if (timeUntilReset <= TimeSpan.Zero)
        {
            return "Resets soon";
        }

        if (timeUntilReset < TimeSpan.FromMinutes(1))
        {
            return "Resets in <1 m";
        }

        if (timeUntilReset < TimeSpan.FromHours(1))
        {
            return string.Create(
                CultureInfo.InvariantCulture, $"Resets in {(int)timeUntilReset.TotalMinutes} m");
        }

        if (timeUntilReset < TimeSpan.FromHours(24))
        {
            return string.Create(
                CultureInfo.InvariantCulture,
                $"Resets in {(int)timeUntilReset.TotalHours} h {timeUntilReset.Minutes:00} m");
        }

        return string.Create(
            CultureInfo.InvariantCulture, $"Resets in {timeUntilReset.Days} d {timeUntilReset.Hours} h");
    }

    /// <summary>"No data yet" | "Updated just now" | "Updated 42 s ago" | "Updated 5 m ago" | "Updated 3 h ago" | "Updated 2 d ago".</summary>
    public static string FormatDataAge(DateTimeOffset? lastRetrievedUtc, DateTimeOffset utcNow)
    {
        if (lastRetrievedUtc is not { } last)
        {
            return "No data yet";
        }

        // Producer/consumer clock skew must never render a negative age.
        var age = utcNow - last;
        if (age < TimeSpan.Zero)
        {
            age = TimeSpan.Zero;
        }

        if (age < TimeSpan.FromSeconds(10))
        {
            return "Updated just now";
        }

        if (age < TimeSpan.FromSeconds(60))
        {
            return string.Create(CultureInfo.InvariantCulture, $"Updated {(int)age.TotalSeconds} s ago");
        }

        if (age < TimeSpan.FromMinutes(60))
        {
            return string.Create(CultureInfo.InvariantCulture, $"Updated {(int)age.TotalMinutes} m ago");
        }

        if (age < TimeSpan.FromHours(24))
        {
            return string.Create(CultureInfo.InvariantCulture, $"Updated {(int)age.TotalHours} h ago");
        }

        return string.Create(CultureInfo.InvariantCulture, $"Updated {(int)age.TotalDays} d ago");
    }

    /// <summary>"Resets Jul 1" — invariant "MMM d".</summary>
    public static string FormatMonthlyReset(DateOnly resetDate) =>
        "Resets " + resetDate.ToString("MMM d", CultureInfo.InvariantCulture);

    /// <summary>"63% used" — rounded to the nearest integer, MidpointRounding.AwayFromZero.</summary>
    public static string FormatPercentUsed(double percentUsed) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"{(int)Math.Round(percentUsed, MidpointRounding.AwayFromZero)}% used");
}
