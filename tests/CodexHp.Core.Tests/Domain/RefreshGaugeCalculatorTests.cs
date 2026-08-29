using CodexHp.Core.Domain;
using Xunit;

namespace CodexHp.Core.Tests.Domain;

public sealed class RefreshGaugeCalculatorTests
{
    [Theory]
    [InlineData(0, 10_000, 10, 0)]
    [InlineData(10_000, 0, 10, 1)]
    [InlineData(10_000, 5_000, 10, 0.5)]
    [InlineData(10_000, 10_000, 10, 0)]
    [InlineData(10_000, 20_000, 10, 0)]
    [InlineData(20_000, 0, 10, 1)]
    public void RemainingFraction_clamps_to_the_quota_window(
        long resetUnixMs,
        long nowUnixMs,
        int windowSeconds,
        double expected)
    {
        Assert.Equal(expected, RefreshGaugeCalculator.RemainingFraction(resetUnixMs, nowUnixMs, windowSeconds), 6);
    }

    [Fact]
    public void RemainingFraction_is_zero_for_invalid_window()
    {
        Assert.Equal(0, RefreshGaugeCalculator.RemainingFraction(10_000, 0, 0));
    }
}
