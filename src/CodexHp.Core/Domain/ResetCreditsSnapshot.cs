using System.Globalization;

namespace CodexHp.Core.Domain;

public sealed record ResetCredit(string Id, long? ExpiresAtUnixMs);

// Null details means unavailable, not zero credits or unlimited lifetime.
public sealed record ResetCreditsSnapshot(int AvailableCount, IReadOnlyList<ResetCredit>? Credits = null)
{
    public bool HasCompleteDetails => this.Credits is { } credits
        && credits.Count == this.AvailableCount
        && credits.Select(credit => credit.Id).Distinct(StringComparer.Ordinal).Count() == credits.Count;
}

public sealed record ResetCreditsDisplayState(
    string CountText,
    string? ExpiryText,
    bool IsUrgent,
    bool IsStale,
    string Tooltip)
{
    public string Text => this.ExpiryText is null ? this.CountText : $"{this.CountText} · {this.ExpiryText}";

    public static ResetCreditsDisplayState Create(ResetCreditsSnapshot? snapshot, long nowUnixMs, bool isStale)
    {
        if (snapshot is null)
        {
            return new("—", null, false, isStale, "Banked reset information unavailable. CodexHp will retry automatically.");
        }

        var count = snapshot.AvailableCount.ToString(CultureInfo.InvariantCulture);
        var lines = new List<string> { $"Banked resets: {count}" };
        var credits = snapshot.Credits?.OrderBy(credit => credit.ExpiresAtUnixMs ?? long.MaxValue).ToArray();
        // The server may cap details. Do not claim an earliest expiry from a partial list.
        var canIdentifyNextExpiry = snapshot.HasCompleteDetails
            && credits is { Length: > 0 }
            && credits.All(credit => IsValidExpiry(credit.ExpiresAtUnixMs));
        var nextExpiry = canIdentifyNextExpiry ? credits![0].ExpiresAtUnixMs : null;
        var remainingMs = nextExpiry is { } expiry ? expiry - nowUnixMs : (long?)null;
        var expiryText = snapshot.AvailableCount == 0 ? null
            : remainingMs is { } remaining ? FormatDuration(remaining) : "—";

        if (snapshot.AvailableCount > 0)
        {
            if (!canIdentifyNextExpiry)
            {
                lines.Add("Next expiry unavailable (missing or incomplete details).");
            }

            if (credits is not null)
            {
                foreach (var credit in credits.Where(credit => IsValidExpiry(credit.ExpiresAtUnixMs)).Take(2))
                {
                    lines.Add($"Expires {DateTimeOffset.FromUnixTimeMilliseconds(credit.ExpiresAtUnixMs!.Value).ToLocalTime():yyyy-MM-dd HH:mm}");
                }
            }
        }

        if (remainingMs <= 0)
        {
            lines.Add("Expiry passed; awaiting updated inventory.");
        }
        if (isStale)
        {
            lines.Add("Banked resets based on last successful update.");
        }
        return new(count, expiryText, remainingMs <= 86_400_000, isStale, string.Join("\r\n", lines));
    }

    private static bool IsValidExpiry(long? expiry) => expiry is > 0
        && expiry <= DateTimeOffset.MaxValue.ToUnixTimeMilliseconds();

    internal static string FormatDuration(long remainingMs)
    {
        if (remainingMs <= 0) return "0m";
        if (remainingMs < 60_000) return "<1m";
        if (remainingMs < 3_600_000) return $"{remainingMs / 60_000}m";
        if (remainingMs < 86_400_000) return $"{remainingMs / 3_600_000}h";
        return $"{remainingMs / 86_400_000}d";
    }
}
