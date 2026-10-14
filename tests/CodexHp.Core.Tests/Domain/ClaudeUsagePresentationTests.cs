using CodexHp.Core.Domain;
using CodexHp.Core.Settings;
using Xunit;

namespace CodexHp.Core.Tests.Domain;

public sealed class ClaudeUsagePresentationTests
{
    private const long Now = 10_000_000_000;

    [Fact]
    public void Claude_always_shows_the_5H_gauge_even_when_banked_resets_are_selected()
    {
        var settings = AppSettings.Default with { UpperBarMode = UpperBarMode.BankedResets };

        var state = Reduce(Fresh(), settings: settings);

        Assert.Null(state.BankedResets);
        Assert.Equal(9, state.ManaBar.RemainingPercent);
        Assert.Equal(87, state.HpBar.RemainingPercent);
        Assert.False(state.ManaBar.IsStale);
    }

    [Fact]
    public void Fresh_Claude_usage_names_the_observation_time_and_source()
    {
        var state = Reduce(Fresh(observedAgoMs: 12 * 60_000));

        var rows = state.TooltipSections[0].Rows;
        Assert.Contains(new OverlayTooltipRow("Updated", "12m ago · cache"), rows);
        Assert.False(state.HpBar.IsStale);
    }

    [Fact]
    public void Claude_usage_older_than_30_minutes_is_dimmed()
    {
        var state = Reduce(Fresh(observedAgoMs: 31 * 60_000));

        Assert.True(state.ManaBar.IsStale);
        Assert.True(state.HpBar.IsStale);
        Assert.Equal(9, state.ManaBar.RemainingPercent);
    }

    [Fact]
    public void Claude_usage_older_than_2_hours_is_hidden()
    {
        var state = Reduce(Fresh(observedAgoMs: 2 * 3_600_000 + 1));

        Assert.Null(state.ManaBar.RemainingPercent);
        Assert.Null(state.HpBar.RemainingPercent);
        Assert.Null(state.ContentMessage);
        Assert.Equal("No recent Claude usage", state.TooltipSections[0].Title);
    }

    [Fact]
    public void Unknown_reset_times_leave_the_reset_gauges_empty()
    {
        var usage = Fresh() with { SessionResetUnixMs = 0, WeeklyResetUnixMs = 0 };

        var state = Reduce(usage);

        Assert.Equal(0, state.ManaBar.RefreshFraction);
        Assert.Equal(0, state.HpBar.RefreshFraction);
        Assert.Contains(new OverlayTooltipRow("Week reset", "Unavailable"), state.TooltipSections[0].Rows);
    }

    [Fact]
    public void A_passed_reset_waits_for_an_update_instead_of_assuming_a_full_allowance()
    {
        var usage = Fresh() with { SessionResetUnixMs = Now - 1 };

        var state = Reduce(usage);

        Assert.Equal(9, state.ManaBar.RemainingPercent);
        Assert.True(state.ManaBar.IsStale);
        Assert.False(state.HpBar.IsStale);
        Assert.Contains(new OverlayTooltipRow("5H reset", "Waiting for update"), state.TooltipSections[0].Rows);
    }

    [Fact]
    public void Missing_Claude_usage_asks_the_user_to_open_the_desktop_app()
    {
        var state = Reduce(null, failed: true);

        Assert.Equal("Open Claude", state.ContentMessage);
    }

    [Fact]
    public void Provider_label_and_other_provider_summary_are_presented()
    {
        var summary = UsageOverlayStateReducer.CreateProviderSummary(
            UsageProvider.Codex,
            UsageProviderState.Current(new UsageSnapshot(78, 42, Now + 1, 18_000, Now + 2, 604_800)),
            ServiceHealthState.Issue,
            Now);

        var state = Reduce(Fresh(), presentation: new ProviderPresentation(UsageProvider.Claude, 1, summary));

        Assert.Equal("Claude", state.ProviderLabel);
        Assert.Equal(1, state.ProviderLabelEmphasis);
        Assert.Equal(new OverlayTooltipRow("Codex", "5H 78% · Week 42% · Service issue"), state.TooltipSections[^1].Rows.Single());
    }

    [Fact]
    public void Claude_service_issues_are_titled_for_Claude()
    {
        var state = Reduce(Fresh(), health: ServiceHealthState.Issue);

        Assert.Contains(state.TooltipSections, section => section.Title == "Claude service issue" && section.IsWarning);
    }

    [Fact]
    public void Show_only_while_running_accepts_the_Claude_desktop_app()
    {
        var settings = AppSettings.Default with { ShowOnlyWhenChatGptRunning = true };

        Assert.True(Reduce(Fresh(), settings: settings, visibility: new(false, false, IsClaudeRunning: true)).IsVisible);
        Assert.False(Reduce(Fresh(), settings: settings, visibility: new(false, false)).IsVisible);
    }

    [Fact]
    public void A_full_Codex_5H_limit_is_left_out_of_the_summary_but_a_full_Claude_one_is_not()
    {
        var full = UsageProviderState.Current(new UsageSnapshot(100, 87, Now + 1, 18_000, Now + 2, 604_800) { ObservedUnixMs = Now });

        Assert.Equal("Week 87%", UsageOverlayStateReducer.CreateProviderSummary(UsageProvider.Codex, full, ServiceHealthState.Operational, Now).Value);
        Assert.Equal("5H 100% · Week 87%", UsageOverlayStateReducer.CreateProviderSummary(UsageProvider.Claude, full, ServiceHealthState.Operational, Now).Value);
    }

    [Fact]
    public void Expired_Claude_usage_is_summarized_as_unavailable()
    {
        var summary = UsageOverlayStateReducer.CreateProviderSummary(
            UsageProvider.Claude,
            UsageProviderState.Current(Fresh(observedAgoMs: 3 * 3_600_000)),
            ServiceHealthState.Operational,
            Now);

        Assert.Equal(new OverlayTooltipRow("Claude", "Unavailable"), summary);
    }

    private static UsageSnapshot Fresh(long observedAgoMs = 60_000) =>
        new(9, 87, Now + 3_600_000, 18_000, Now + 200_000_000, 604_800)
        {
            ObservedUnixMs = Now - observedAgoMs,
            Source = "cache",
        };

    private static UsageOverlayState Reduce(
        UsageSnapshot? usage,
        bool failed = false,
        AppSettings? settings = null,
        ServiceHealthState health = ServiceHealthState.Operational,
        VisibilityState? visibility = null,
        ProviderPresentation? presentation = null) => UsageOverlayStateReducer.Reduce(
            usage is null
                ? failed ? UsageProviderState.Failed() : UsageProviderState.Waiting
                : UsageProviderState.Current(usage),
            TokenActivityProviderState.Waiting,
            health,
            "Elevated errors",
            visibility ?? new VisibilityState(true, false),
            settings ?? AppSettings.Default,
            Now,
            presentation: presentation ?? new ProviderPresentation(UsageProvider.Claude));
}
