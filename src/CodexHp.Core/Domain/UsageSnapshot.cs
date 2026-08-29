namespace CodexHp.Core.Domain;

public sealed record UsageSnapshot(
    int SessionRemainingPercent,
    int WeeklyRemainingPercent,
    long SessionResetUnixMs,
    int SessionWindowSeconds,
    long WeeklyResetUnixMs,
    int WeeklyWindowSeconds);

public enum ProviderAvailability
{
    Waiting,
    Current,
    Failed,
}

public sealed record UsageProviderState(ProviderAvailability Availability, UsageSnapshot? LastSuccessful)
{
    public static UsageProviderState Waiting { get; } = new(ProviderAvailability.Waiting, null);

    public static UsageProviderState Current(UsageSnapshot snapshot) =>
        new(ProviderAvailability.Current, snapshot ?? throw new ArgumentNullException(nameof(snapshot)));

    public static UsageProviderState Failed(UsageSnapshot? lastSuccessful = null) =>
        new(ProviderAvailability.Failed, lastSuccessful);
}
