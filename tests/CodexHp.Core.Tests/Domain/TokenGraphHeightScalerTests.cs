using CodexHp.Core.Domain;
using Xunit;

namespace CodexHp.Core.Tests.Domain;

public sealed class TokenGraphHeightScalerTests
{
    [Fact]
    public void Knee_is_ten_thousand_tokens()
    {
        Assert.Equal(10_000, TokenGraphHeightScaler.KneeTokenCount);
    }

    [Theory]
    [InlineData(1_000, 45_172, 58, 3)]
    [InlineData(3_677, 45_172, 58, 10)]
    [InlineData(5_000, 45_172, 58, 13)]
    [InlineData(10_000, 45_172, 58, 23)]
    [InlineData(20_000, 45_172, 58, 37)]
    [InlineData(45_172, 45_172, 58, 58)]
    public void Scale_uses_a_ten_thousand_token_soft_log_knee(
        int tokens,
        int maximumTokens,
        int maximumHeight,
        int expected)
    {
        Assert.Equal(
            expected,
            TokenGraphHeightScaler.Scale(tokens, maximumTokens, maximumHeight));
    }

    [Theory]
    [InlineData(0, 45_172, 58)]
    [InlineData(-1, 45_172, 58)]
    [InlineData(1_000, 0, 58)]
    [InlineData(1_000, -1, 58)]
    [InlineData(1_000, 45_172, 0)]
    [InlineData(1_000, 45_172, -1)]
    public void Scale_returns_zero_when_a_required_dimension_is_not_positive(
        int tokens,
        int maximumTokens,
        int maximumHeight)
    {
        Assert.Equal(0, TokenGraphHeightScaler.Scale(tokens, maximumTokens, maximumHeight));
    }

    [Fact]
    public void Scale_keeps_positive_activity_visible_at_one_pixel()
    {
        Assert.Equal(1, TokenGraphHeightScaler.Scale(1, int.MaxValue, 58));
    }

    [Fact]
    public void Scale_caps_values_above_the_visible_maximum()
    {
        Assert.Equal(58, TokenGraphHeightScaler.Scale(60_000, 45_172, 58));
    }
}
