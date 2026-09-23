using CodexHp.Core.Domain;
using CodexHp.Core.Settings;
using Xunit;

namespace CodexHp.Core.Tests.Domain;

public sealed class OverlayTooltipSectionsTests
{
    [Theory]
    [InlineData(UpperBarMode.BankedResets, 100, false)]
    [InlineData(UpperBarMode.BankedResets, 99, true)]
    [InlineData(UpperBarMode.FiveHourUsage, 100, false)]
    [InlineData(UpperBarMode.FiveHourUsage, 30, true)]
    public void Structured_tooltip_preserves_week_first_and_5H_visibility(UpperBarMode mode, int remaining, bool show5H)
    {
        var state = Create(mode, remaining);
        Assert.Equal("Week resets in", state.TooltipSections[0].Rows[0].Label);
        Assert.Equal(show5H, state.TooltipSections.SelectMany(section => section.Rows).Any(row => row.Label.StartsWith("5H")));
        Assert.Equal(mode == UpperBarMode.BankedResets, state.TooltipSections.Any(section => section.Title == "Banked resets"));
        Assert.True(state.TooltipSections[^1].IsWarning);
    }

    [Fact]
    public void Credit_rows_keep_only_two_earliest_local_expiries_without_offset()
    {
        const long now = 1_000_000;
        var snapshot = new ResetCreditsSnapshot(3, [new("c", now + 300_000), new("a", now + 100_000), new("b", now + 200_000)]);
        var rows = ResetCreditsDisplayState.Create(snapshot, now, false).TooltipRows.Where(row => row.Label == "Expires").ToArray();
        Assert.Equal(2, rows.Length);
        Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(now + 100_000).ToLocalTime().ToString("yyyy-MM-dd HH:mm"), rows[0].Value);
        Assert.Equal(16, rows[0].Value.Length);
    }

    [Fact]
    public void Zero_unknown_and_stale_remain_distinct()
    {
        Assert.Equal("0", ResetCreditsDisplayState.Create(new(0, []), 0, false).TooltipRows[0].Value);
        Assert.Equal("—", ResetCreditsDisplayState.Create(null, 0, false).TooltipRows[0].Value);
        Assert.Contains(ResetCreditsDisplayState.Create(new(2), 0, true).TooltipRows,
            row => row.Value.Contains("last successful"));
    }

    private static UsageOverlayState Create(UpperBarMode mode, int remaining) => UsageOverlayStateReducer.Reduce(
        UsageProviderState.Current(new(remaining, 70, 3_600_000, 18_000, 86_400_000, 604_800)
        { ResetCredits = new(0, []) }), TokenActivityProviderState.Waiting,
        ServiceHealthState.Issue, "Partial System Degradation", new(true, false),
        AppSettings.Default with { UpperBarMode = mode }, 1_000_000,
        affectedServiceComponentGroups: [new("Codex", ["CLI"])]);
}
