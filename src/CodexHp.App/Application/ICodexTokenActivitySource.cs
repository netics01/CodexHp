namespace CodexHp.App.Application;

public interface ITokenActivitySource
{
    IReadOnlyList<int> ReadRecentTokenBuckets(long nowUnixMs, int bucketSeconds, int maxBuckets);
}

public interface ICodexTokenActivitySource : ITokenActivitySource
{
}
