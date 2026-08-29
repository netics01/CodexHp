namespace CodexHp.App.Application;

public interface ICodexTokenActivitySource
{
    IReadOnlyList<int> ReadRecentTokenBuckets(long nowUnixMs, int bucketSeconds, int maxBuckets);
}
