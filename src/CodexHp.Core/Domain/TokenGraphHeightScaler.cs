namespace CodexHp.Core.Domain;

public static class TokenGraphHeightScaler
{
    public const int KneeTokenCount = 10_000;

    public static int Scale(int tokens, int maximumTokens, int maximumHeight)
    {
        if (tokens <= 0 || maximumTokens <= 0 || maximumHeight <= 0)
        {
            return 0;
        }

        if (tokens >= maximumTokens)
        {
            return maximumHeight;
        }

        var numerator = Math.Log(1d + (tokens / (double)KneeTokenCount));
        var denominator = Math.Log(1d + (maximumTokens / (double)KneeTokenCount));
        return Math.Max(1, (int)Math.Floor(maximumHeight * numerator / denominator));
    }
}
