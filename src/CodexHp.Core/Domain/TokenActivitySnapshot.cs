namespace CodexHp.Core.Domain;

public sealed record TokenActivitySnapshot(IReadOnlyList<int> Buckets);

public sealed record TokenActivityProviderState(
    ProviderAvailability Availability,
    TokenActivitySnapshot? LastSuccessful)
{
    public static TokenActivityProviderState Waiting { get; } = new(ProviderAvailability.Waiting, null);

    public static TokenActivityProviderState Failed { get; } = new(ProviderAvailability.Failed, null);

    public static TokenActivityProviderState Current(IReadOnlyList<int> buckets)
    {
        ArgumentNullException.ThrowIfNull(buckets);
        return new TokenActivityProviderState(
            ProviderAvailability.Current,
            new TokenActivitySnapshot(buckets.ToArray()));
    }
}
