using CodexHp.Core.Domain;
using CodexHp.Core.Settings;
using Xunit;

namespace CodexHp.Core.Tests.Domain;

public sealed class UsageOverlayStateReducerTests
{
    private const long NowUnixMs = 1_000_000;

    [Fact]
    public void Reduce_shows_placeholders_without_usage_but_keeps_current_graph()
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
        Assert.Equal("OpenAI service issue: Partial System Degradation", state.StatusStripeTooltip);
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

        Assert.Equal("OpenAI service issue detected.", state.StatusStripeTooltip);
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
