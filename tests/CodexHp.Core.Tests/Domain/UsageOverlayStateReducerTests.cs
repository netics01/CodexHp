using CodexHp.Core.Domain;
using CodexHp.Core.Settings;
using Xunit;

namespace CodexHp.Core.Tests.Domain;

public sealed class UsageOverlayStateReducerTests
{
    private const long NowUnixMs = 1_000_000;

    [Theory]
    [InlineData(ServiceHealthState.Operational)]
    [InlineData(ServiceHealthState.Unknown)]
    public void Reset_tooltip_shows_week_before_5H_without_a_service_issue(ServiceHealthState health)
    {
        var state = ReduceResetTooltip(SampleResetUsage(), health: health);

        Assert.Equal("Week resets in 3d 4h 12m\r\n5H resets in 2h 18m", state.Tooltip);
        Assert.Null(state.StatusStripeTooltip);
        Assert.Null(state.ContentMessage);
    }

    [Fact]
    public void Reset_tooltip_omits_only_5H_when_both_usage_bars_are_full()
    {
        var usage = SampleResetUsage() with { SessionRemainingPercent = 100, WeeklyRemainingPercent = 100 };

        Assert.Equal("Week resets in 3d 4h 12m", ReduceResetTooltip(usage).Tooltip);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(99)]
    public void Reset_tooltip_does_not_hide_5H_when_the_refresh_gauge_is_full(int remainingPercent)
    {
        var usage = SampleResetUsage() with
        {
            SessionRemainingPercent = remainingPercent,
            SessionResetUnixMs = NowUnixMs + 18_000_000,
        };
        var state = ReduceResetTooltip(usage);

        Assert.Equal(1d, state.ManaBar.RefreshFraction);
        Assert.EndsWith("5H resets in 5h", state.Tooltip);
    }

    [Theory]
    [InlineData(70, "Week resets in 3d 4h 12m\r\n5H resets in 2h 18m")]
    [InlineData(100, "Week resets in 3d 4h 12m")]
    public void Service_issue_follows_reset_times_after_one_blank_line(int remainingPercent, string resetText)
    {
        var usage = SampleResetUsage() with { SessionRemainingPercent = remainingPercent };
        var state = ReduceResetTooltip(usage, health: ServiceHealthState.Issue);

        Assert.Equal(
            resetText + "\r\n\r\nOpenAI service issue: Partial System Degradation\r\nChatGPT - Search\r\nCodex - CLI",
            state.Tooltip);
    }

    [Theory]
    [InlineData(86_400_000, "1d")]
    [InlineData(3_600_000, "1h")]
    [InlineData(60_000, "1m")]
    [InlineData(59_999, "<1m")]
    [InlineData(1, "<1m")]
    public void Reset_tooltip_formats_time_boundaries(long remainingMs, string expected)
    {
        var usage = SampleResetUsage() with
        {
            SessionRemainingPercent = 100,
            WeeklyResetUnixMs = NowUnixMs + remainingMs,
        };

        Assert.Equal($"Week resets in {expected}", ReduceResetTooltip(usage).Tooltip);
    }

    [Theory]
    [InlineData(0, "Week reset time unavailable")]
    [InlineData(long.MaxValue, "Week reset time unavailable")]
    [InlineData(NowUnixMs, "Week reset pending")]
    [InlineData(NowUnixMs - 1, "Week reset pending")]
    public void Reset_tooltip_handles_missing_or_elapsed_reset_times(long resetUnixMs, string expected)
    {
        var usage = SampleResetUsage() with
        {
            SessionRemainingPercent = 100,
            WeeklyResetUnixMs = resetUnixMs,
        };

        Assert.Equal(expected, ReduceResetTooltip(usage).Tooltip);
    }

    [Fact]
    public void Reset_tooltip_recalculates_from_current_time_without_a_new_usage_snapshot()
    {
        var usage = SampleResetUsage();

        Assert.Equal(
            "Week resets in 3d 4h 11m\r\n5H resets in 2h 17m",
            ReduceResetTooltip(usage, nowUnixMs: NowUnixMs + 60_000).Tooltip);
    }

    [Fact]
    public void Failed_usage_poll_keeps_countdown_but_identifies_last_known_reset_times()
    {
        var state = UsageOverlayStateReducer.Reduce(
            UsageProviderState.Failed(SampleResetUsage()), TokenActivityProviderState.Waiting,
            ServiceHealthState.Operational, string.Empty, new VisibilityState(true, false),
            AppSettings.Default, NowUnixMs);

        Assert.Equal(
            "Week resets in 3d 4h 12m\r\n5H resets in 2h 18m\r\nReset times based on last successful update.",
            state.Tooltip);
    }

    private static UsageSnapshot SampleResetUsage() => new(
        70, 40, NowUnixMs + 8_280_000, 18_000, NowUnixMs + 274_320_000, 604_800);

    private static UsageOverlayState ReduceResetTooltip(
        UsageSnapshot usage,
        ServiceHealthState health = ServiceHealthState.Operational,
        long nowUnixMs = NowUnixMs) => UsageOverlayStateReducer.Reduce(
            UsageProviderState.Current(usage), TokenActivityProviderState.Waiting,
            health, "Partial System Degradation", new VisibilityState(true, false),
            AppSettings.Default, nowUnixMs,
            affectedServiceComponentGroups:
            [
                new ServiceStatusComponentGroup("ChatGPT", ["Search"]),
                new ServiceStatusComponentGroup("Codex", ["CLI"]),
            ]);

    [Fact]
    public void Reduce_shows_loading_without_usage_but_keeps_current_graph_state()
    {
        var state = UsageOverlayStateReducer.Reduce(
            UsageProviderState.Waiting,
            TokenActivityProviderState.Current([10, 20]),
            ServiceHealthState.Operational,
            string.Empty,
            new VisibilityState(IsChatGptRunning: false, IsFullscreenOnOverlayMonitor: false),
            AppSettings.Default,
            NowUnixMs);

        Assert.True(state.IsVisible);
        Assert.Null(state.ManaBar.RemainingPercent);
        Assert.Null(state.HpBar.RemainingPercent);
        Assert.False(state.ManaBar.IsStale);
        Assert.Equal([10, 20], state.TokenBuckets);
        Assert.Null(state.StatusStripeColor);
        Assert.Equal("Loading…", state.ContentMessage);
    }

    [Fact]
    public void Reduce_keeps_last_usage_and_marks_it_stale_after_failure()
    {
        var usage = new UsageSnapshot(
            SessionRemainingPercent: 70,
            WeeklyRemainingPercent: 40,
            SessionResetUnixMs: NowUnixMs + 9_000,
            SessionWindowSeconds: 10,
            WeeklyResetUnixMs: NowUnixMs + 15_000,
            WeeklyWindowSeconds: 20);

        var state = UsageOverlayStateReducer.Reduce(
            UsageProviderState.Failed(usage),
            TokenActivityProviderState.Failed,
            ServiceHealthState.Unknown,
            string.Empty,
            new VisibilityState(IsChatGptRunning: true, IsFullscreenOnOverlayMonitor: false),
            AppSettings.Default,
            NowUnixMs);

        Assert.Equal(70, state.ManaBar.RemainingPercent);
        Assert.Equal(40, state.HpBar.RemainingPercent);
        Assert.True(state.ManaBar.IsStale);
        Assert.True(state.HpBar.IsStale);
        Assert.Equal(0.9, state.ManaBar.RefreshFraction, 6);
        Assert.Equal(0.75, state.HpBar.RefreshFraction, 6);
        Assert.Empty(state.TokenBuckets);
        Assert.Equal(AppSettings.Default.Colors.ServiceUnknown, state.StatusStripeColor);
        Assert.Null(state.ContentMessage);
    }

    [Fact]
    public void Reduce_explains_that_missing_Codex_auth_can_follow_installation_or_sign_in()
    {
        var state = UsageOverlayStateReducer.Reduce(
            UsageProviderState.Failed(failureReason: UsageFailureReason.SignInRequired),
            TokenActivityProviderState.Failed,
            ServiceHealthState.Operational,
            string.Empty,
            new VisibilityState(false, false),
            AppSettings.Default,
            NowUnixMs);

        Assert.Equal("Sign in to Codex", state.ContentMessage);
        Assert.Equal(
            "Codex authentication was not found. Install or open Codex, then sign in. CodexHp will detect the sign-in automatically.",
            state.ContentTooltip);
    }

    [Fact]
    public void Reduce_maps_service_issue_to_the_configured_stripe_color()
    {
        var state = UsageOverlayStateReducer.Reduce(
            UsageProviderState.Waiting,
            TokenActivityProviderState.Failed,
            ServiceHealthState.Issue,
            "Partial System Degradation",
            new VisibilityState(false, false),
            AppSettings.Default,
            NowUnixMs);

        Assert.Equal(AppSettings.Default.Colors.ServiceIssue, state.StatusStripeColor);
        Assert.Equal(
            "OpenAI service issue: Partial System Degradation\r\nAffected component details unavailable",
            state.StatusStripeTooltip);
    }

    [Fact]
    public void Reduce_appends_affected_components_to_the_service_issue_tooltip()
    {
        var state = UsageOverlayStateReducer.Reduce(
            UsageProviderState.Waiting,
            TokenActivityProviderState.Failed,
            ServiceHealthState.Issue,
            "Partial System Degradation",
            new VisibilityState(false, false),
            AppSettings.Default,
            NowUnixMs,
            affectedServiceComponents: ["ChatGPT", "Codex"]);

        Assert.Equal(
            "OpenAI service issue: Partial System Degradation\r\nChatGPT, Codex",
            state.StatusStripeTooltip);
    }

    [Fact]
    public void Reduce_combines_one_affected_group_with_its_components()
    {
        var state = UsageOverlayStateReducer.Reduce(
            UsageProviderState.Waiting,
            TokenActivityProviderState.Failed,
            ServiceHealthState.Issue,
            "Partial System Degradation",
            new VisibilityState(false, false),
            AppSettings.Default,
            NowUnixMs,
            affectedServiceComponents: ["Responses"],
            affectedServiceGroups: ["APIs"]);

        Assert.Equal(
            "OpenAI service issue: Partial System Degradation\r\nAPIs — Responses",
            state.StatusStripeTooltip);
    }

    [Fact]
    public void Reduce_displays_a_group_when_components_are_unavailable()
    {
        var state = UsageOverlayStateReducer.Reduce(
            UsageProviderState.Waiting,
            TokenActivityProviderState.Failed,
            ServiceHealthState.Issue,
            "Partial System Degradation",
            new VisibilityState(false, false),
            AppSettings.Default,
            NowUnixMs,
            affectedServiceComponents: [],
            affectedServiceGroups: ["APIs"]);

        Assert.Equal(
            "OpenAI service issue: Partial System Degradation\r\nAPIs",
            state.StatusStripeTooltip);
    }

    [Fact]
    public void Reduce_keeps_multiple_groups_and_components_on_separate_labeled_lines()
    {
        var state = UsageOverlayStateReducer.Reduce(
            UsageProviderState.Waiting,
            TokenActivityProviderState.Failed,
            ServiceHealthState.Issue,
            "Partial System Degradation",
            new VisibilityState(false, false),
            AppSettings.Default,
            NowUnixMs,
            affectedServiceComponents: ["Responses", "Conversations"],
            affectedServiceGroups: ["APIs", "ChatGPT"]);

        Assert.Equal(
            "OpenAI service issue: Partial System Degradation\r\nAffected groups: APIs, ChatGPT\r\nAffected components: Responses, Conversations",
            state.StatusStripeTooltip);
    }

    [Fact]
    public void Reduce_displays_each_affected_product_group_on_its_own_line()
    {
        var state = UsageOverlayStateReducer.Reduce(
            UsageProviderState.Waiting,
            TokenActivityProviderState.Failed,
            ServiceHealthState.Issue,
            "Partial System Degradation",
            new VisibilityState(false, false),
            AppSettings.Default,
            NowUnixMs,
            affectedServiceComponents: ["Search", "Codex Web", "Sites", "CLI"],
            affectedServiceGroups: ["ChatGPT", "Codex"],
            affectedServiceComponentGroups:
            [
                new ServiceStatusComponentGroup("ChatGPT", ["Search", "Sites"]),
                new ServiceStatusComponentGroup("Codex", ["Codex Web", "CLI"]),
            ]);

        Assert.Equal(
            "OpenAI service issue: Partial System Degradation\r\nChatGPT - Search, Sites\r\nCodex - Codex Web, CLI",
            state.StatusStripeTooltip);
    }

    [Theory]
    [InlineData(ServiceHealthState.Operational, "All Systems Operational")]
    [InlineData(ServiceHealthState.Unknown, "")]
    public void Reduce_hides_the_status_stripe_tooltip_when_service_status_is_not_an_issue(
        ServiceHealthState health,
        string description)
    {
        var state = UsageOverlayStateReducer.Reduce(
            UsageProviderState.Waiting,
            TokenActivityProviderState.Failed,
            health,
            description,
            new VisibilityState(false, false),
            AppSettings.Default,
            NowUnixMs);

        Assert.Null(state.StatusStripeTooltip);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Reduce_uses_an_english_fallback_when_an_issue_has_no_description(string description)
    {
        var state = UsageOverlayStateReducer.Reduce(
            UsageProviderState.Waiting,
            TokenActivityProviderState.Failed,
            ServiceHealthState.Issue,
            description,
            new VisibilityState(false, false),
            AppSettings.Default,
            NowUnixMs);

        Assert.Equal(
            "OpenAI service issue detected.\r\nAffected component details unavailable",
            state.StatusStripeTooltip);
    }

    [Theory]
    [InlineData(false, false, false, true)]
    [InlineData(true, false, false, false)]
    [InlineData(true, true, false, true)]
    [InlineData(false, true, true, false)]
    [InlineData(true, true, true, false)]
    public void Reduce_applies_visibility_and_same_monitor_fullscreen_priority(
        bool showOnlyWhenChatGptRunning,
        bool isChatGptRunning,
        bool isFullscreenOnOverlayMonitor,
        bool expectedVisible)
    {
        var settings = AppSettings.Default with
        {
            ShowOnlyWhenChatGptRunning = showOnlyWhenChatGptRunning,
        };

        var state = UsageOverlayStateReducer.Reduce(
            UsageProviderState.Waiting,
            TokenActivityProviderState.Failed,
            ServiceHealthState.Operational,
            string.Empty,
            new VisibilityState(isChatGptRunning, isFullscreenOnOverlayMonitor),
            settings,
            NowUnixMs);

        Assert.Equal(expectedVisible, state.IsVisible);
    }
}
