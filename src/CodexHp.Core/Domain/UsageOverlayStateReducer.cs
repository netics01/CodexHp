using CodexHp.Core.Settings;

namespace CodexHp.Core.Domain;

public static class UsageOverlayStateReducer
{
    // Claude values come from the desktop app's own refreshes; age decides how much to trust them.
    public const long ClaudeStaleAfterMs = 30 * 60 * 1000L;
    public const long ClaudeExpiredAfterMs = 2 * 60 * 60 * 1000L;

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
        IReadOnlyList<ServiceStatusComponentGroup>? affectedServiceComponentGroups = null,
        ProviderPresentation? presentation = null)
    {
        ArgumentNullException.ThrowIfNull(usage);
        ArgumentNullException.ThrowIfNull(tokenActivity);
        ArgumentNullException.ThrowIfNull(serviceStatusDescription);
        ArgumentNullException.ThrowIfNull(visibility);
        ArgumentNullException.ThrowIfNull(settings);

        var isClaude = presentation?.Provider == UsageProvider.Claude;
        var isVisible = !visibility.IsFullscreenOnOverlayMonitor
            && (!settings.ShowOnlyWhenChatGptRunning || visibility.IsChatGptRunning || visibility.IsClaudeRunning);
        var observationAgeMs = isClaude && usage.LastSuccessful?.ObservedUnixMs is { } observedUnixMs
            ? Math.Max(0, nowUnixMs - observedUnixMs)
            : (long?)null;
        var isExpired = observationAgeMs > ClaudeExpiredAfterMs;
        var isUsageStale = (usage.Availability == ProviderAvailability.Failed && usage.LastSuccessful is not null)
            || observationAgeMs > ClaudeStaleAfterMs;
        var snapshot = isExpired ? null : usage.LastSuccessful;
        var manaBar = CreateGauge(
            snapshot?.SessionRemainingPercent,
            snapshot?.SessionResetUnixMs ?? 0,
            snapshot?.SessionWindowSeconds ?? 0,
            isUsageStale || (isClaude && IsResetPassed(snapshot?.SessionResetUnixMs, nowUnixMs)),
            nowUnixMs);
        var hpBar = CreateGauge(
            snapshot?.WeeklyRemainingPercent,
            snapshot?.WeeklyResetUnixMs ?? 0,
            snapshot?.WeeklyWindowSeconds ?? 0,
            isUsageStale || (isClaude && IsResetPassed(snapshot?.WeeklyResetUnixMs, nowUnixMs)),
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
                isClaude ? "Claude" : "OpenAI",
                serviceStatusDescription,
                affectedServiceComponents,
                affectedServiceGroups,
                affectedServiceComponentGroups)
            : ((string Text, OverlayTooltipSection Section)?)null;
        var statusStripeTooltip = serviceTooltip?.Text;
        var contentStatus = isClaude
            ? CreateClaudeContentStatus(usage, snapshot, isExpired, observationAgeMs, nowUnixMs)
            : CreateContentStatus(usage, nowUnixMs);
        var bankedResets = !isClaude && settings.UpperBarMode == UpperBarMode.BankedResets
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
                CreateResetRow("Week", snapshot.WeeklyResetUnixMs, nowUnixMs, isClaude),
            };
            if (manaBar.RemainingPercent < 100)
            {
                resetRows.Add(CreateResetRow("5H", snapshot.SessionResetUnixMs, nowUnixMs, isClaude));
                if (bankedResets is not null)
                    resetRows.Add(new("5H remaining", $"{manaBar.RemainingPercent}%"));
            }
            if (observationAgeMs is { } ageMs)
                resetRows.Add(new("Updated", FormatObservation(ageMs, snapshot.Source)));
            if (isUsageStale && !isClaude)
                resetRows.Add(new("", "Reset times based on last successful update."));
            tooltipSections.Add(new(null, resetRows));
            if (bankedResets is not null)
                tooltipSections.Add(new("Banked resets", bankedResets.TooltipRows));
        }
        else if (isExpired && observationAgeMs is { } expiredAgeMs)
        {
            tooltipSections.Add(new("No recent Claude usage",
            [
                new("Updated", FormatObservation(expiredAgeMs, usage.LastSuccessful?.Source)),
                new("", "Open the Claude desktop app to refresh usage."),
            ]));
        }
        else if (contentStatus.Tooltip is not null)
        {
            tooltipSections.Add(new(contentStatus.Message, [new("", contentStatus.Tooltip)]));
        }
        if (serviceTooltip is { } issue)
        {
            tooltipSections.Add(issue.Section);
        }
        if (presentation?.OtherProviderSummary is { } summary)
        {
            tooltipSections.Add(new(null, [summary]));
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
            ProviderLabel = presentation is null ? null : ProviderName(presentation.Provider),
            IsProviderLabelEmphasized = presentation?.IsLabelEmphasized ?? false,
        };
    }

    public static string ProviderName(UsageProvider provider) =>
        provider == UsageProvider.Claude ? "Claude" : "Codex";

    // One-line summary of the provider that is not shown, for the bottom of the tooltip.
    public static OverlayTooltipRow CreateProviderSummary(
        UsageProvider provider,
        UsageProviderState usage,
        ServiceHealthState serviceHealth,
        long nowUnixMs)
    {
        ArgumentNullException.ThrowIfNull(usage);
        var snapshot = usage.LastSuccessful;
        if (provider == UsageProvider.Claude
            && snapshot?.ObservedUnixMs is { } observedUnixMs
            && nowUnixMs - observedUnixMs > ClaudeExpiredAfterMs)
        {
            snapshot = null;
        }

        var parts = new List<string>();
        if (snapshot is null)
        {
            parts.Add("Unavailable");
        }
        else
        {
            parts.Add($"5H {Math.Clamp(snapshot.SessionRemainingPercent, 0, 100)}%");
            parts.Add($"Week {Math.Clamp(snapshot.WeeklyRemainingPercent, 0, 100)}%");
        }

        if (serviceHealth == ServiceHealthState.Issue)
        {
            parts.Add("Service issue");
        }

        return new OverlayTooltipRow(ProviderName(provider), string.Join(" · ", parts));
    }

    private static bool IsResetPassed(long? resetUnixMs, long nowUnixMs) =>
        resetUnixMs is > 0 && resetUnixMs <= nowUnixMs;

    private static (string? Message, string? Tooltip) CreateClaudeContentStatus(
        UsageProviderState usage,
        UsageSnapshot? snapshot,
        bool isExpired,
        long? observationAgeMs,
        long nowUnixMs)
    {
        if (snapshot is not null)
        {
            var lines = new List<string>
            {
                FormatResetTime("Week", snapshot.WeeklyResetUnixMs, nowUnixMs, isClaude: true),
            };
            if (Math.Clamp(snapshot.SessionRemainingPercent, 0, 100) < 100)
            {
                lines.Add(FormatResetTime("5H", snapshot.SessionResetUnixMs, nowUnixMs, isClaude: true));
            }

            if (observationAgeMs is { } ageMs)
            {
                lines.Add($"Updated {FormatObservation(ageMs, snapshot.Source)}");
            }

            return (null, string.Join("\r\n", lines));
        }

        if (isExpired)
        {
            return (null, "No recent Claude usage. Open the Claude desktop app to refresh usage.");
        }

        return usage.Availability == ProviderAvailability.Waiting
            ? ("Loading…", "CodexHp is checking Claude usage.")
            : ("Open Claude", "Claude usage appears after the Claude desktop app updates it. Open the Claude desktop app.");
    }

    private static string FormatObservation(long ageMs, string? source)
    {
        var age = ageMs < 60_000
            ? "<1m ago"
            : ageMs < 3_600_000
                ? $"{ageMs / 60_000}m ago"
                : $"{ageMs / 3_600_000}h {ageMs % 3_600_000 / 60_000}m ago";
        return string.IsNullOrWhiteSpace(source) ? age : $"{age} · {source}";
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

    private static OverlayTooltipRow CreateResetRow(string label, long resetUnixMs, long nowUnixMs, bool isClaude = false)
    {
        var value = FormatResetValue(resetUnixMs, nowUnixMs, isClaude);
        return new(value is "Unavailable" or "Pending" or "Waiting for update" ? $"{label} reset" : $"{label} resets in", value);
    }

    private static string FormatResetTime(string label, long resetUnixMs, long nowUnixMs, bool isClaude = false)
    {
        var value = FormatResetValue(resetUnixMs, nowUnixMs, isClaude);
        return value switch
        {
            "Unavailable" => $"{label} reset time unavailable",
            "Pending" => $"{label} reset pending",
            "Waiting for update" => $"{label} reset passed; waiting for update",
            _ => $"{label} resets in {value}",
        };
    }

    // Claude values are only as new as the desktop app's last refresh; a passed reset is not
    // proof of a full allowance until the app reports again.
    private static string FormatResetValue(long resetUnixMs, long nowUnixMs, bool isClaude = false)
    {
        if (isClaude && resetUnixMs > 0 && resetUnixMs <= nowUnixMs)
        {
            return "Waiting for update";
        }

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
        string serviceName,
        string serviceStatusDescription,
        IReadOnlyList<string>? affectedServiceComponents,
        IReadOnlyList<string>? affectedServiceGroups,
        IReadOnlyList<ServiceStatusComponentGroup>? affectedServiceComponentGroups)
    {
        var issueText = string.IsNullOrWhiteSpace(serviceStatusDescription)
            ? $"{serviceName} service issue detected."
            : $"{serviceName} service issue: {serviceStatusDescription.Trim()}";
        (string, OverlayTooltipSection) Finish(IEnumerable<string> details)
        {
            var lines = details.ToArray();
            var rows = new List<OverlayTooltipRow>();
            if (!string.IsNullOrWhiteSpace(serviceStatusDescription))
                rows.Add(new("", serviceStatusDescription.Trim()));
            rows.AddRange(lines.Select(line => new OverlayTooltipRow("", line)));
            return ($"{issueText}\r\n{string.Join("\r\n", lines)}", new($"{serviceName} service issue", rows, true));
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
