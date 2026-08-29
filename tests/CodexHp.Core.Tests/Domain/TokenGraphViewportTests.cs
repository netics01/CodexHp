using CodexHp.Core.Domain;
using CodexHp.Core.Settings;
using Xunit;

namespace CodexHp.Core.Tests.Domain;

public sealed class TokenGraphViewportTests
{
    [Fact]
    public void Default_appearance_fills_the_complete_graph_viewport()
    {
        var appearance = AppearanceSettings.Default;

        Assert.Equal(15, TokenGraphViewport.BucketSeconds);
        Assert.Equal(54, TokenGraphViewport.ChartLeft(appearance));
        Assert.Equal(138, TokenGraphViewport.ChartRight(appearance));
        Assert.Equal(84, TokenGraphViewport.CalculateVisibleBucketCount(appearance));
        Assert.Equal(
            TimeSpan.FromMinutes(21),
            TokenGraphViewport.CalculateVisibleDuration(appearance));
    }

    [Fact]
    public void Bar_width_and_gap_reduce_the_visible_history_capacity()
    {
        var appearance = AppearanceSettings.Default with
        {
            OverlayWidth = 400,
            GraphBarWidth = 5,
            GraphBarGap = 2,
        };

        Assert.Equal(48, TokenGraphViewport.CalculateVisibleBucketCount(appearance));
        Assert.Equal(
            TimeSpan.FromMinutes(12),
            TokenGraphViewport.CalculateVisibleDuration(appearance));
    }

    [Fact]
    public void Viewport_too_narrow_for_one_bar_has_zero_visible_duration()
    {
        var appearance = new AppearanceSettings(
            OverlayWidth: 120,
            OverlayHeight: 68,
            GaugePaneWidth: 100,
            GraphBarWidth: 20,
            GraphBarGap: 0,
            StatusStripeWidth: 4);

        Assert.Equal(0, TokenGraphViewport.CalculateVisibleBucketCount(appearance));
        Assert.Equal(TimeSpan.Zero, TokenGraphViewport.CalculateVisibleDuration(appearance));
    }
}
