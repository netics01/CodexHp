using CodexHp.Core.Domain;
using CodexHp.Core.Settings;

namespace CodexHp.App.Presentation;

public readonly record struct LayoutRect(int Left, int Top, int Width, int Height)
{
    public int Right => Left + Width;

    public int Bottom => Top + Height;
}

public enum OverlayDrawKind
{
    Rectangle,
    Text,
}

public enum OverlayElementRole
{
    Background,
    StatusStripe,
    ManaTrack,
    ManaFill,
    ManaText,
    ManaRefreshTrack,
    ManaRefreshFill,
    HpTrack,
    HpFill,
    HpText,
    HpRefreshTrack,
    HpRefreshFill,
    GraphGridDot,
    GraphBaseline,
    TokenBar,
    ContentMessage,
    OverlayPositionOutline,
}

public sealed record OverlayDrawCommand(
    OverlayDrawKind Kind,
    OverlayElementRole Role,
    LayoutRect Bounds,
    ColorValue Color,
    double Opacity = 1,
    string? Text = null,
    int FontSize = 0);

public sealed record UsageOverlayLayout(
    int Width,
    int Height,
    IReadOnlyList<OverlayDrawCommand> Commands);

public sealed record OverlayPresentationSettings(
    ColorSettings Colors,
    EffectiveAppearanceSettings Appearance,
    double DisplayScaleY = 1)
{
    public static OverlayPresentationSettings FromUnscaled(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var appearance = settings.Appearance;
        return new OverlayPresentationSettings(
            settings.Colors,
            new EffectiveAppearanceSettings(
                appearance.OverlayWidth,
                appearance.OverlayHeight,
                appearance.GaugePaneWidth,
                appearance.GraphBarWidth,
                appearance.GraphBarGap,
                appearance.StatusStripeWidth),
            DisplayScaleY: 1);
    }
}

public static class UsageOverlayRenderer
{
    private const int RefreshHeight = 2;
    private const int GaugeGroupGap = 2;
    private const int ReferenceGaugeRowHeight = 27;
    private const int ReferenceGaugeFontHeight = 16;
    private const int MinimumGaugeFontDip = 8;
    private const double StaleOpacity = 0.55;
    private static readonly ColorValue BackgroundColor = ColorValue.Parse("#18181C");
    private static readonly ColorValue GaugeTrackColor = ColorValue.Parse("#3E3E44");
    private static readonly ColorValue RefreshTrackColor = ColorValue.Parse("#44464E");
    private static readonly ColorValue White = ColorValue.Parse("#FFFFFF");
    private static readonly ColorValue GridColor = ColorValue.Parse("#808080");

    public static UsageOverlayLayout CreateLayout(
        UsageOverlayState state,
        AppSettings settings,
        bool isOverlayPositionChangeMode)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return CreateLayout(state, OverlayPresentationSettings.FromUnscaled(settings), isOverlayPositionChangeMode);
    }

    public static UsageOverlayLayout CreateLayout(
        UsageOverlayState state,
        OverlayPresentationSettings settings,
        bool isOverlayPositionChangeMode)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(settings);

        var appearance = settings.Appearance;
        var width = appearance.OverlayWidth;
        var height = appearance.OverlayHeight;
        var commands = new List<OverlayDrawCommand>
        {
            Rectangle(OverlayElementRole.Background, new LayoutRect(0, 0, width, height), BackgroundColor),
        };

        var gaugePaneWidth = Math.Clamp(appearance.GaugePaneWidth, 20, Math.Max(20, width - 20));
        var stripeOffset = 0;
        if (state.StatusStripeColor is { } stripeColor && appearance.StatusStripeWidth > 0)
        {
            var stripeBounds = new LayoutRect(4, 2, appearance.StatusStripeWidth, Math.Max(1, height - 4));
            commands.Add(Rectangle(OverlayElementRole.StatusStripe, stripeBounds, stripeColor));
            stripeOffset = appearance.StatusStripeWidth + 2;
        }

        if (!string.IsNullOrWhiteSpace(state.ContentMessage))
        {
            commands.Add(new OverlayDrawCommand(
                OverlayDrawKind.Text,
                OverlayElementRole.ContentMessage,
                new LayoutRect(4 + stripeOffset, 0, Math.Max(1, width - 8 - stripeOffset), height),
                White,
                Text: state.ContentMessage,
                FontSize: Math.Clamp(height / 3, 10, 16)));
            if (isOverlayPositionChangeMode)
            {
                AddOverlayPositionOutline(commands, width, height);
            }

            return new UsageOverlayLayout(width, height, commands);
        }

        var gaugeLeft = 4 + stripeOffset;
        var gaugeRight = gaugePaneWidth - 3;
        var gaugeTop = 2;
        var gaugeBottom = height - 2;
        var gaugeHeight = Math.Max(1, gaugeBottom - gaugeTop);
        var quotaHeight = Math.Max(
            1,
            (gaugeHeight - (RefreshHeight * 2) - GaugeGroupGap) / 2);
        var gaugeWidth = Math.Max(1, gaugeRight - gaugeLeft);
        var manaBounds = new LayoutRect(gaugeLeft, gaugeTop, gaugeWidth, quotaHeight);
        var manaRefreshBounds = new LayoutRect(
            gaugeLeft,
            manaBounds.Bottom,
            gaugeWidth,
            RefreshHeight);
        var hpBounds = new LayoutRect(
            gaugeLeft,
            manaRefreshBounds.Bottom + GaugeGroupGap,
            gaugeWidth,
            quotaHeight);
        var hpRefreshTop = hpBounds.Bottom;
        var hpRefreshBounds = new LayoutRect(
            gaugeLeft,
            hpRefreshTop,
            gaugeWidth,
            Math.Max(1, Math.Min(RefreshHeight, gaugeBottom - hpRefreshTop)));
        var fontSize = CalculateGaugeFontHeight(quotaHeight, settings.DisplayScaleY);

        AddGauge(
            commands,
            state.ManaBar,
            manaBounds,
            manaRefreshBounds,
            settings.Colors.ManaBar,
            settings.Colors.RefreshGauge,
            OverlayElementRole.ManaTrack,
            OverlayElementRole.ManaFill,
            OverlayElementRole.ManaText,
            OverlayElementRole.ManaRefreshTrack,
            OverlayElementRole.ManaRefreshFill,
            fontSize);
        AddGauge(
            commands,
            state.HpBar,
            hpBounds,
            hpRefreshBounds,
            settings.Colors.HpBar,
            settings.Colors.RefreshGauge,
            OverlayElementRole.HpTrack,
            OverlayElementRole.HpFill,
            OverlayElementRole.HpText,
            OverlayElementRole.HpRefreshTrack,
            OverlayElementRole.HpRefreshFill,
            fontSize);

        AddGraph(commands, state.TokenBuckets, settings, height);

        if (isOverlayPositionChangeMode)
        {
            AddOverlayPositionOutline(commands, width, height);
        }

        return new UsageOverlayLayout(width, height, commands);
    }

    private static int CalculateGaugeFontHeight(int gaugeRowHeight, double displayScaleY)
    {
        var proportionalHeight = Math.Max(
            1,
            (int)Math.Round(
                gaugeRowHeight * ReferenceGaugeFontHeight / (double)ReferenceGaugeRowHeight,
                MidpointRounding.AwayFromZero));
        var validDisplayScale = double.IsFinite(displayScaleY) && displayScaleY > 0
            ? displayScaleY
            : 1;
        var minimumHeight = (int)Math.Round(
            MinimumGaugeFontDip * validDisplayScale,
            MidpointRounding.AwayFromZero);
        return Math.Max(proportionalHeight, minimumHeight);
    }

    private static void AddGauge(
        ICollection<OverlayDrawCommand> commands,
        GaugeDisplayState gauge,
        LayoutRect quotaBounds,
        LayoutRect refreshBounds,
        ColorValue quotaColor,
        ColorValue refreshColor,
        OverlayElementRole trackRole,
        OverlayElementRole fillRole,
        OverlayElementRole textRole,
        OverlayElementRole refreshTrackRole,
        OverlayElementRole refreshFillRole,
        int fontSize)
    {
        var opacity = gauge.IsStale ? StaleOpacity : 1;
        var remainingPercent = Math.Clamp(gauge.RemainingPercent ?? 0, 0, 100);
        var refreshFraction = Math.Clamp(gauge.RefreshFraction, 0, 1);
        commands.Add(Rectangle(trackRole, quotaBounds, GaugeTrackColor, opacity));
        commands.Add(Rectangle(
            fillRole,
            quotaBounds with { Width = quotaBounds.Width * remainingPercent / 100 },
            quotaColor,
            opacity));
        commands.Add(new OverlayDrawCommand(
            OverlayDrawKind.Text,
            textRole,
            quotaBounds,
            White,
            opacity,
            gauge.RemainingPercent is { } percent ? $"{Math.Clamp(percent, 0, 100)}%" : "--%",
            fontSize));
        commands.Add(Rectangle(refreshTrackRole, refreshBounds, RefreshTrackColor, opacity));
        commands.Add(Rectangle(
            refreshFillRole,
            refreshBounds with { Width = (int)Math.Floor(refreshBounds.Width * refreshFraction) },
            refreshColor,
            opacity));
    }

    private static void AddGraph(
        ICollection<OverlayDrawCommand> commands,
        IReadOnlyList<int> buckets,
        OverlayPresentationSettings settings,
        int overlayHeight)
    {
        var chartLeft = TokenGraphViewport.ChartLeft(settings.Appearance);
        var chartRight = TokenGraphViewport.ChartRight(settings.Appearance);
        var chartTop = 4;
        var baselineTop = overlayHeight - 6;
        var chartBottom = baselineTop;
        if (chartRight <= chartLeft || chartBottom <= chartTop)
        {
            return;
        }

        var barWidth = Math.Max(1, settings.Appearance.GraphBarWidth);
        var gap = Math.Max(0, settings.Appearance.GraphBarGap);
        var slotWidth = barWidth + gap;
        const int bucketsPerFiveMinutes = 300 / TokenGraphViewport.BucketSeconds;
        for (var bucket = bucketsPerFiveMinutes; ; bucket += bucketsPerFiveMinutes)
        {
            var x = chartRight - (bucket * slotWidth);
            if (x < chartLeft)
            {
                break;
            }

            for (var y = chartTop; y < chartBottom; y += 4)
            {
                commands.Add(Rectangle(
                    OverlayElementRole.GraphGridDot,
                    new LayoutRect(x, y, 1, Math.Min(2, chartBottom - y)),
                    GridColor));
            }
        }

        commands.Add(Rectangle(
            OverlayElementRole.GraphBaseline,
            new LayoutRect(chartLeft, baselineTop, chartRight - chartLeft, 1),
            White));

        var maximumBucket = buckets.Count == 0 ? 0 : Math.Max(0, buckets.Max());
        var xPosition = chartRight - barWidth;
        if (maximumBucket <= 0)
        {
            return;
        }

        for (var index = buckets.Count - 1; index >= 0 && xPosition >= chartLeft; index--)
        {
            var value = Math.Max(0, buckets[index]);
            if (value > 0)
            {
                var barHeight = TokenGraphHeightScaler.Scale(
                    value,
                    maximumBucket,
                    chartBottom - chartTop);
                commands.Add(Rectangle(
                    OverlayElementRole.TokenBar,
                    new LayoutRect(xPosition, chartBottom - barHeight, barWidth, barHeight),
                    TokenColorInterpolator.Interpolate(
                        value,
                        settings.Colors.TokenLow,
                        settings.Colors.TokenHigh)));
            }

            xPosition -= slotWidth;
        }
    }

    private static void AddOverlayPositionOutline(
        ICollection<OverlayDrawCommand> commands,
        int width,
        int height)
    {
        const int thickness = 4;
        commands.Add(Rectangle(OverlayElementRole.OverlayPositionOutline, new LayoutRect(0, 0, width, thickness), White));
        commands.Add(Rectangle(OverlayElementRole.OverlayPositionOutline, new LayoutRect(0, height - thickness, width, thickness), White));
        commands.Add(Rectangle(OverlayElementRole.OverlayPositionOutline, new LayoutRect(0, thickness, thickness, height - (thickness * 2)), White));
        commands.Add(Rectangle(OverlayElementRole.OverlayPositionOutline, new LayoutRect(width - thickness, thickness, thickness, height - (thickness * 2)), White));
    }

    private static OverlayDrawCommand Rectangle(
        OverlayElementRole role,
        LayoutRect bounds,
        ColorValue color,
        double opacity = 1) =>
        new(OverlayDrawKind.Rectangle, role, bounds, color, opacity);
}
