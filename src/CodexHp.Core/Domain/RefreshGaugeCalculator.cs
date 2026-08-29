namespace CodexHp.Core.Domain;

public static class RefreshGaugeCalculator
{
    public static double RemainingFraction(long resetUnixMs, long nowUnixMs, int windowSeconds)
    {
        if (resetUnixMs <= 0 || windowSeconds <= 0)
        {
            return 0;
        }

        var windowMs = windowSeconds * 1000d;
        var remainingMs = Math.Clamp(resetUnixMs - nowUnixMs, 0d, windowMs);
        return remainingMs / windowMs;
    }
}
