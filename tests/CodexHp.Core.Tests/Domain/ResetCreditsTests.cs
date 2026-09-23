using CodexHp.Core.Domain;
using CodexHp.Core.Settings;
using Xunit;

namespace CodexHp.Core.Tests.Domain;

public sealed class ResetCreditsTests
{
    private static readonly long Now = new DateTimeOffset(2026, 9, 23, 10, 0, 0, TimeSpan.Zero).ToUnixTimeMilliseconds();

    [Theory]
    [InlineData(11 * 86_400_000L + 59_999, "11d", false)]
    [InlineData(86_400_000L, "1d", true)]
    [InlineData(86_399_999L, "23h", true)]
    [InlineData(3_600_000L, "1h", true)]
    [InlineData(3_599_999L, "59m", true)]
    [InlineData(60_000L, "1m", true)]
    [InlineData(59_999L, "<1m", true)]
    [InlineData(0L, "0m", true)]
    [InlineData(-60_000L, "0m", true)]
    public void Earliest_expiry_uses_whole_units_without_changing_inventory(long remaining, string text, bool urgent)
    {
        var snapshot = new ResetCreditsSnapshot(2, [new("later", Now + 20 * 86_400_000L), new("first", Now + remaining)]);
        var state = ResetCreditsDisplayState.Create(snapshot, Now, false);
        Assert.Equal($"2 · {text}", state.Text);
        Assert.Equal(urgent, state.IsUrgent);
        Assert.Equal(remaining <= 0, state.Tooltip.Contains("awaiting updated inventory", StringComparison.Ordinal));
    }

    [Fact]
    public void Unknown_zero_and_missing_expiry_are_distinct()
    {
        Assert.Equal("—", ResetCreditsDisplayState.Create(null, Now, false).Text);
        Assert.Equal("0", ResetCreditsDisplayState.Create(new(0, []), Now, false).Text);
        var missing = ResetCreditsDisplayState.Create(new(2), Now, false);
        Assert.Equal("2 · —", missing.Text);
        Assert.False(missing.IsUrgent);
        Assert.Contains("unavailable", missing.Tooltip);
    }

    [Fact]
    public void Tooltip_shows_only_the_two_nearest_known_expiries_in_ascending_order()
    {
        var snapshot = new ResetCreditsSnapshot(3,
        [
            new("middle", Now + 20 * 86_400_000L),
            new("last", Now + 30 * 86_400_000L),
            new("first", Now + 10 * 86_400_000L),
        ]);
        var state = ResetCreditsDisplayState.Create(snapshot, Now, false);
        Assert.Equal("3 · 10d", state.Text);
        Assert.Equal(new[]
        {
            "Banked resets: 3",
            $"Expires {DateTimeOffset.FromUnixTimeMilliseconds(Now + 10 * 86_400_000L).ToLocalTime():yyyy-MM-dd HH:mm}",
            $"Expires {DateTimeOffset.FromUnixTimeMilliseconds(Now + 20 * 86_400_000L).ToLocalTime():yyyy-MM-dd HH:mm}",
        }, state.Tooltip.Split("\r\n"));
    }

    [Fact]
    public void Missing_expiries_do_not_displace_known_dates_or_hide_the_incomplete_data_warning()
    {
        var snapshot = new ResetCreditsSnapshot(3,
        [new("known", Now + 86_400_000), new("missing", null), new("invalid", long.MaxValue)]);
        var state = ResetCreditsDisplayState.Create(snapshot, Now, false);
        Assert.Equal("3 · —", state.Text);
        Assert.Contains("missing or incomplete details", state.Tooltip);
        Assert.Single(state.Tooltip.Split("\r\n"), line => line.StartsWith("Expires ", StringComparison.Ordinal));
        Assert.DoesNotContain("Use is subject", state.Tooltip);
        Assert.Equal("Banked resets: 0", ResetCreditsDisplayState.Create(new(0, []), Now, false).Tooltip);
    }

    [Fact]
    public void Partial_duplicate_or_unknown_expiries_never_claim_an_earliest_deadline()
    {
        foreach (var snapshot in new ResetCreditsSnapshot[]
        {
            new(3, [new("one", Now + 86_400_000)]),
            new(2, [new("one", Now + 86_400_000), new("one", Now + 86_400_000)]),
            new(2, [new("one", Now + 86_400_000), new("two", null)]),
            new(1, [new("one", long.MaxValue)]),
        })
        {
            var state = ResetCreditsDisplayState.Create(snapshot, Now, false);
            Assert.Equal("—", state.ExpiryText);
            Assert.False(state.IsUrgent);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(99)]
    [InlineData(100)]
    public void Standard_tooltip_keeps_week_first_and_hides_all_5H_information_when_full(int remainingPercent)
    {
        var usage = new UsageSnapshot(remainingPercent, 70, Now + 3_600_000, 18_000, Now + 86_400_000, 604_800)
        { ResetCredits = new(1, [new("one", Now + 2 * 86_400_000L)]) };
        var settings = AppSettings.Default with { UpperBarMode = UpperBarMode.BankedResets };
        UsageOverlayState Reduce(AppSettings selected, UsageProviderState provider) => UsageOverlayStateReducer.Reduce(
            provider, TokenActivityProviderState.Waiting, ServiceHealthState.Issue, "Degraded",
            new(false, false), selected, Now, ["Responses"], ["APIs"]);
        var state = Reduce(settings, UsageProviderState.Current(usage));
        Assert.Equal("1 · 2d", state.BankedResets!.Text);
        Assert.StartsWith("Week resets in 1d\r\n", state.Tooltip);
        if (remainingPercent == 100)
        {
            Assert.DoesNotContain("5H", state.Tooltip);
        }
        else
        {
            Assert.Contains("5H resets in 1h", state.Tooltip);
            Assert.Contains($"5H remaining: {remainingPercent}%", state.Tooltip);
        }
        Assert.Contains("Banked resets: 1", state.Tooltip);
        Assert.DoesNotContain("Use is subject", state.Tooltip);
        Assert.EndsWith("OpenAI service issue: Degraded\r\nAPIs — Responses", state.Tooltip);
        var stale = Reduce(settings, UsageProviderState.Failed(usage));
        Assert.True(stale.BankedResets!.IsStale);
        Assert.Contains("Banked resets based on last successful update", stale.Tooltip);
        Assert.Equal(remainingPercent < 100, stale.Tooltip!.Contains("5H", StringComparison.Ordinal));
        Assert.Null(Reduce(AppSettings.Default, UsageProviderState.Current(usage)).BankedResets);
    }
}
