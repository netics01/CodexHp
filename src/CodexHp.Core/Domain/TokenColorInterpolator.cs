using CodexHp.Core.Settings;

namespace CodexHp.Core.Domain;

public static class TokenColorInterpolator
{
    public const int LowTokenThreshold = 10_000;
    public const int HighTokenThreshold = 100_000;

    public static ColorValue Interpolate(int tokens, ColorValue low, ColorValue high)
    {
        var fraction = Math.Clamp(
            (tokens - LowTokenThreshold) / (double)(HighTokenThreshold - LowTokenThreshold),
            0,
            1);

        return new ColorValue(
            InterpolateChannel(low.Red, high.Red, fraction),
            InterpolateChannel(low.Green, high.Green, fraction),
            InterpolateChannel(low.Blue, high.Blue, fraction));
    }

    private static byte InterpolateChannel(byte low, byte high, double fraction) =>
        checked((byte)Math.Round(low + ((high - low) * fraction), MidpointRounding.AwayFromZero));
}
