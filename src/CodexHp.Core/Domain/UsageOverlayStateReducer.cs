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
        IReadOnlyList<string>? affectedServiceComponents = null,
        IReadOnlyList<string>? affectedServiceGroups = null)
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
            ? BuildServiceIssueTooltip(
                serviceStatusDescription,
                affectedServiceComponents,
                affectedServiceGroups)
            : null;
        var contentStatus = CreateContentStatus(usage);

        return new UsageOverlayState(
            isVisible,
            manaBar,
            hpBar,
            buckets,
            stripeColor,
            statusStripeTooltip,
            contentStatus.Message,
            contentStatus.Tooltip);
    }

    private static (string? Message, string? Tooltip) CreateContentStatus(UsageProviderState usage)
    {
        if (usage.LastSuccessful is not null)
        {
            return (null, null);
        }

        return usage.Availability switch
        {
            ProviderAvailability.Waiting => (
                "Loading…",
                "CodexHp is checking Codex usage."),
            ProviderAvailability.Failed => usage.FailureReason switch
            {
                UsageFailureReason.SignInRequired => (
                    "Sign in to Codex",
                    "Codex authentication was not found. Install or open Codex, then sign in. CodexHp will detect the sign-in automatically."),
                UsageFailureReason.ReconnectRequired => (
                    "Reconnect Codex",
                    "Codex authentication could not be read. Open Codex and sign in again. CodexHp will retry automatically."),
                _ => (
                    "Usage unavailable",
                    "Codex usage is temporarily unavailable. CodexHp will retry automatically."),
            },
            _ => (null, null),
        };
    }

    private static string BuildServiceIssueTooltip(
        string serviceStatusDescription,
        IReadOnlyList<string>? affectedServiceComponents,
        IReadOnlyList<string>? affectedServiceGroups)
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
        var groupNames = affectedServiceGroups?
            .Where(group => !string.IsNullOrWhiteSpace(group))
            .Select(group => group.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray()
            ?? [];

        if (groupNames.Length == 1 && componentNames.Length > 0)
        {
            return $"{issueText}\r\n{groupNames[0]} — {string.Join(", ", componentNames)}";
        }

        if (groupNames.Length > 0 && componentNames.Length > 0)
        {
            return $"{issueText}\r\nAffected groups: {string.Join(", ", groupNames)}\r\nAffected components: {string.Join(", ", componentNames)}";
        }

        if (groupNames.Length == 1)
        {
            return $"{issueText}\r\n{groupNames[0]}";
        }

        if (groupNames.Length > 1)
        {
            return $"{issueText}\r\nAffected groups: {string.Join(", ", groupNames)}";
        }

        return componentNames.Length > 0
            ? $"{issueText}\r\n{string.Join(", ", componentNames)}"
            : $"{issueText}\r\nAffected component details unavailable";
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
