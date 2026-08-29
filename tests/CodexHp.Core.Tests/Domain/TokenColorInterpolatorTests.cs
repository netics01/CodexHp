using CodexHp.Core.Domain;
using CodexHp.Core.Settings;
using Xunit;

namespace CodexHp.Core.Tests.Domain;

public sealed class TokenColorInterpolatorTests
{
    private static readonly ColorValue Low = ColorValue.Parse("#2667CD");
    private static readonly ColorValue High = ColorValue.Parse("#DC4856");

    [Theory]
    [InlineData(0, "#2667CD")]
    [InlineData(10_000, "#2667CD")]
    [InlineData(55_000, "#815892")]
    [InlineData(100_000, "#DC4856")]
    [InlineData(150_000, "#DC4856")]
    public void Interpolate_uses_approved_thresholds(int tokens, string expected)
    {
        var color = TokenColorInterpolator.Interpolate(tokens, Low, High);

        Assert.Equal(expected, color.ToHex());
    }
}
