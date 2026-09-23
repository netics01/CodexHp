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
        IReadOnlyList<string>? affectedServiceGroups = null,
        IReadOnlyList<ServiceStatusComponentGroup>? affectedServiceComponentGroups = null)
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
                affectedServiceGroups,
                affectedServiceComponentGroups)
            : null;
        var contentStatus = CreateContentStatus(usage, nowUnixMs);
        var bankedResets = settings.UpperBarMode == UpperBarMode.BankedResets
            ? ResetCreditsDisplayState.Create(snapshot?.ResetCredits, nowUnixMs, isUsageStale)
            : null;
        if (bankedResets is not null && snapshot is not null)
        {
            var sessionRemaining = Math.Clamp(snapshot.SessionRemainingPercent, 0, 100);
            if (sessionRemaining < 100)
            {
                contentStatus.Tooltip += $"\r\n5H remaining: {sessionRemaining}%";
            }
            contentStatus.Tooltip += $"\r\n\r\n{bankedResets.Tooltip}";
        }

        return new UsageOverlayState(
            isVisible,
            manaBar,
            hpBar,
            buckets,
            stripeColor,
            statusStripeTooltip,
            contentStatus.Message,
            contentStatus.Tooltip) { ServiceHealth = serviceHealth, BankedResets = bankedResets };
    }

    private static (string? Message, string? Tooltip) CreateContentStatus(
        UsageProviderState usage,
        long nowUnixMs)
    {
        if (usage.LastSuccessful is { } snapshot)
        {
            var lines = new List<string>
            {
                FormatResetTime("Week", snapshot.WeeklyResetUnixMs, nowUnixMs),
            };
            if (Math.Clamp(snapshot.SessionRemainingPercent, 0, 100) < 100)
            {
                lines.Add(FormatResetTime("5H", snapshot.SessionResetUnixMs, nowUnixMs));
            }

            if (usage.Availability == ProviderAvailability.Failed)
            {
                lines.Add("Reset times based on last successful update.");
            }

            return (null, string.Join("\r\n", lines));
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

    private static string FormatResetTime(string label, long resetUnixMs, long nowUnixMs)
    {
        // A missing 5H window is represented by long.MaxValue in the usage client.
        if (resetUnixMs <= 0 || resetUnixMs > DateTimeOffset.MaxValue.ToUnixTimeMilliseconds())
        {
            return $"{label} reset time unavailable";
        }

        if (resetUnixMs <= nowUnixMs)
        {
            return $"{label} reset pending";
        }

        var remaining = TimeSpan.FromMilliseconds(resetUnixMs - nowUnixMs);
        if (remaining.TotalMinutes < 1)
        {
            return $"{label} resets in <1m";
        }

        var duration = new List<string>();
        if (remaining.Days > 0)
        {
            duration.Add($"{remaining.Days}d");
        }

        if (remaining.Hours > 0)
        {
            duration.Add($"{remaining.Hours}h");
        }

        if (remaining.Minutes > 0)
        {
            duration.Add($"{remaining.Minutes}m");
        }

        return $"{label} resets in {string.Join(" ", duration)}";
    }

    private static string BuildServiceIssueTooltip(
        string serviceStatusDescription,
        IReadOnlyList<string>? affectedServiceComponents,
        IReadOnlyList<string>? affectedServiceGroups,
        IReadOnlyList<ServiceStatusComponentGroup>? affectedServiceComponentGroups)
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
        var componentGroups = affectedServiceComponentGroups?
            .Where(group => !string.IsNullOrWhiteSpace(group.Name))
            .Select(group => new ServiceStatusComponentGroup(
                group.Name.Trim(),
                group.Components
                    .Where(component => !string.IsNullOrWhiteSpace(component))
                    .Select(component => component.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray()))
            .Where(group => group.Components.Count > 0)
            .DistinctBy(group => group.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray()
            ?? [];

        if (componentGroups.Length > 0)
        {
            var lines = componentGroups
                .Select(group => $"{group.Name} - {string.Join(", ", group.Components)}")
                .ToList();
            var groupedComponentNames = componentGroups
                .SelectMany(group => group.Components)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var ungroupedComponentNames = componentNames
                .Where(component => !groupedComponentNames.Contains(component))
                .ToArray();
            if (ungroupedComponentNames.Length > 0)
            {
                lines.Add($"Affected components: {string.Join(", ", ungroupedComponentNames)}");
            }

            return $"{issueText}\r\n{string.Join("\r\n", lines)}";
        }

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
