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
        var serviceTooltip = serviceHealth == ServiceHealthState.Issue
            ? BuildServiceIssueTooltip(
                serviceStatusDescription,
                affectedServiceComponents,
                affectedServiceGroups,
                affectedServiceComponentGroups)
            : ((string Text, OverlayTooltipSection Section)?)null;
        var statusStripeTooltip = serviceTooltip?.Text;
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

        var tooltipSections = new List<OverlayTooltipSection>();
        if (snapshot is not null)
        {
            var resetRows = new List<OverlayTooltipRow>
            {
                CreateResetRow("Week", snapshot.WeeklyResetUnixMs, nowUnixMs),
            };
            if (manaBar.RemainingPercent < 100)
            {
                resetRows.Add(CreateResetRow("5H", snapshot.SessionResetUnixMs, nowUnixMs));
                if (bankedResets is not null)
                    resetRows.Add(new("5H remaining", $"{manaBar.RemainingPercent}%"));
            }
            if (isUsageStale)
                resetRows.Add(new("", "Reset times based on last successful update."));
            tooltipSections.Add(new(null, resetRows));
            if (bankedResets is not null)
                tooltipSections.Add(new("Banked resets", bankedResets.TooltipRows));
        }
        else if (contentStatus.Tooltip is not null)
        {
            tooltipSections.Add(new(contentStatus.Message, [new("", contentStatus.Tooltip)]));
        }
        if (serviceTooltip is { } issue)
        {
            tooltipSections.Add(issue.Section);
        }

        return new UsageOverlayState(
            isVisible,
            manaBar,
            hpBar,
            buckets,
            stripeColor,
            statusStripeTooltip,
            contentStatus.Message,
            contentStatus.Tooltip)
        {
            ServiceHealth = serviceHealth,
            BankedResets = bankedResets,
            TooltipSections = tooltipSections,
        };
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

    private static OverlayTooltipRow CreateResetRow(string label, long resetUnixMs, long nowUnixMs)
    {
        var value = FormatResetValue(resetUnixMs, nowUnixMs);
        return new(value is "Unavailable" or "Pending" ? $"{label} reset" : $"{label} resets in", value);
    }

    private static string FormatResetTime(string label, long resetUnixMs, long nowUnixMs)
    {
        var value = FormatResetValue(resetUnixMs, nowUnixMs);
        return value switch
        {
            "Unavailable" => $"{label} reset time unavailable",
            "Pending" => $"{label} reset pending",
            _ => $"{label} resets in {value}",
        };
    }

    private static string FormatResetValue(long resetUnixMs, long nowUnixMs)
    {
        // A missing 5H window is represented by long.MaxValue in the usage client.
        if (resetUnixMs <= 0 || resetUnixMs > DateTimeOffset.MaxValue.ToUnixTimeMilliseconds())
        {
            return "Unavailable";
        }

        if (resetUnixMs <= nowUnixMs)
        {
            return "Pending";
        }

        var remaining = TimeSpan.FromMilliseconds(resetUnixMs - nowUnixMs);
        if (remaining.TotalMinutes < 1)
        {
            return "<1m";
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

        return string.Join(" ", duration);
    }

    private static (string Text, OverlayTooltipSection Section) BuildServiceIssueTooltip(
        string serviceStatusDescription,
        IReadOnlyList<string>? affectedServiceComponents,
        IReadOnlyList<string>? affectedServiceGroups,
        IReadOnlyList<ServiceStatusComponentGroup>? affectedServiceComponentGroups)
    {
        var issueText = string.IsNullOrWhiteSpace(serviceStatusDescription)
            ? "OpenAI service issue detected."
            : $"OpenAI service issue: {serviceStatusDescription.Trim()}";
        (string, OverlayTooltipSection) Finish(IEnumerable<string> details)
        {
            var lines = details.ToArray();
            var rows = new List<OverlayTooltipRow>();
            if (!string.IsNullOrWhiteSpace(serviceStatusDescription))
                rows.Add(new("", serviceStatusDescription.Trim()));
            rows.AddRange(lines.Select(line => new OverlayTooltipRow("", line)));
            return ($"{issueText}\r\n{string.Join("\r\n", lines)}", new("OpenAI service issue", rows, true));
        }
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

            return Finish(lines);
        }

        if (groupNames.Length == 1 && componentNames.Length > 0)
        {
            return Finish([$"{groupNames[0]} — {string.Join(", ", componentNames)}"]);
        }

        if (groupNames.Length > 0 && componentNames.Length > 0)
        {
            return Finish([$"Affected groups: {string.Join(", ", groupNames)}", $"Affected components: {string.Join(", ", componentNames)}"]);
        }

        if (groupNames.Length == 1)
        {
            return Finish([groupNames[0]]);
        }

        if (groupNames.Length > 1)
        {
            return Finish([$"Affected groups: {string.Join(", ", groupNames)}"]);
        }

        return Finish([componentNames.Length > 0
            ? string.Join(", ", componentNames)
            : "Affected component details unavailable"]);
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
