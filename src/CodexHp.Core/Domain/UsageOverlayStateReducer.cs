using CodexHp.Core.Settings;

namespace CodexHp.Core.Domain;

public static class UsageOverlayStateReducer
{
    public static UsageOverlayState Reduce(
        UsageProviderState usage,
        TokenActivityProviderState tokenActivity,
        ServiceHealthState serviceHealth,
        string serviceStatusDescription,
        VisibilityState visibility,
        AppSettings settings,
        long nowUnixMs,
        IReadOnlyList<string>? affectedServiceComponents = null)
    {
        ArgumentNullException.ThrowIfNull(usage);
        ArgumentNullException.ThrowIfNull(tokenActivity);
        ArgumentNullException.ThrowIfNull(serviceStatusDescription);
        ArgumentNullException.ThrowIfNull(visibility);
        ArgumentNullException.ThrowIfNull(settings);

        var isVisible = !visibility.IsFullscreenOnOverlayMonitor
            && (!settings.ShowOnlyWhenChatGptRunning || visibility.IsChatGptRunning);
        var isUsageStale = usage.Availability == ProviderAvailability.Failed
            && usage.LastSuccessful is not null;
        var snapshot = usage.LastSuccessful;
        var manaBar = CreateGauge(
            snapshot?.SessionRemainingPercent,
            snapshot?.SessionResetUnixMs ?? 0,
            snapshot?.SessionWindowSeconds ?? 0,
            isUsageStale,
            nowUnixMs);
        var hpBar = CreateGauge(
            snapshot?.WeeklyRemainingPercent,
            snapshot?.WeeklyResetUnixMs ?? 0,
            snapshot?.WeeklyWindowSeconds ?? 0,
            isUsageStale,
            nowUnixMs);
        var buckets = tokenActivity.Availability == ProviderAvailability.Current
            ? tokenActivity.LastSuccessful?.Buckets ?? []
            : [];
        var stripeColor = serviceHealth switch
        {
            ServiceHealthState.Operational => (ColorValue?)null,
            ServiceHealthState.Issue => settings.Colors.ServiceIssue,
            _ => settings.Colors.ServiceUnknown,
        };
        var statusStripeTooltip = serviceHealth == ServiceHealthState.Issue
            ? BuildServiceIssueTooltip(serviceStatusDescription, affectedServiceComponents)
            : null;

        return new UsageOverlayState(
            isVisible,
            manaBar,
            hpBar,
            buckets,
            stripeColor,
            statusStripeTooltip);
    }

    private static string BuildServiceIssueTooltip(
        string serviceStatusDescription,
        IReadOnlyList<string>? affectedServiceComponents)
    {
        var issueText = string.IsNullOrWhiteSpace(serviceStatusDescription)
            ? "OpenAI service issue detected."
            : $"OpenAI service issue: {serviceStatusDescription.Trim()}";
        var componentNames = affectedServiceComponents?
            .Where(component => !string.IsNullOrWhiteSpace(component))
            .Select(component => component.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray()
            ?? [];

        return componentNames.Length == 0
            ? issueText
            : $"{issueText}\r\n{string.Join(", ", componentNames)}";
    }

    private static GaugeDisplayState CreateGauge(
        int? remainingPercent,
        long resetUnixMs,
        int windowSeconds,
        bool isStale,
        long nowUnixMs)
    {
        return new GaugeDisplayState(
            remainingPercent is null ? null : Math.Clamp(remainingPercent.Value, 0, 100),
            RefreshGaugeCalculator.RemainingFraction(resetUnixMs, nowUnixMs, windowSeconds),
            isStale);
    }
}
