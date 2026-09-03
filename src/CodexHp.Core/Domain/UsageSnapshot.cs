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

public enum UsageFailureReason
{
    Unavailable,
    SignInRequired,
    ReconnectRequired,
}

public sealed record UsageProviderState(
    ProviderAvailability Availability,
    UsageSnapshot? LastSuccessful,
    UsageFailureReason? FailureReason)
{
    public static UsageProviderState Waiting { get; } = new(ProviderAvailability.Waiting, null, null);

    public static UsageProviderState Current(UsageSnapshot snapshot) =>
        new(ProviderAvailability.Current, snapshot ?? throw new ArgumentNullException(nameof(snapshot)), null);

    public static UsageProviderState Failed(
        UsageSnapshot? lastSuccessful = null,
        UsageFailureReason failureReason = UsageFailureReason.Unavailable) =>
        new(ProviderAvailability.Failed, lastSuccessful, failureReason);
}
