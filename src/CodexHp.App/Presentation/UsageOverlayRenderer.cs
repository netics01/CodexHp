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
    ManaRefreshSeparator,
    HpTrack,
    HpFill,
    HpText,
    HpRefreshTrack,
    HpRefreshFill,
    HpRefreshSeparator,
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
    int FontSize = 0,
    LayoutRect? ClipBounds = null);

public sealed record UsageOverlayLayout(
    int Width,
    int Height,
    IReadOnlyList<OverlayDrawCommand> Commands);

public sealed record OverlayPresentationSettings(
    ColorSettings Colors,
    EffectiveAppearanceSettings Appearance,
    bool IsLight = false)
{
    public static OverlayPresentationSettings FromUnscaled(AppSettings settings, bool systemIsLight = false)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var appearance = settings.Appearance;
        return new OverlayPresentationSettings(
            settings.GetColors(systemIsLight),
            new EffectiveAppearanceSettings(
                appearance.OverlayWidth,
                appearance.OverlayHeight,
                appearance.GaugePaneWidth,
                appearance.GraphBarWidth,
                appearance.GraphBarGap,
                appearance.StatusStripeWidth),
            settings.UsesLightColors(systemIsLight));
    }
}

public static class UsageOverlayRenderer
{
    private const int ReferenceGaugeRowHeight = 27;
    private const int ReferenceGaugeFontHeight = 16;
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
        int X(double dip) => OverlayPixelPolicy.ToPixels(dip, appearance.DisplayScaleX);
        int Y(double dip) => OverlayPixelPolicy.ToPixels(dip, appearance.DisplayScaleY);
        var refreshHeight = Y(OverlayPixelPolicy.RefreshHeightDip);
        var gaugeGroupGap = Y(OverlayPixelPolicy.GaugeGroupGapDip);
        var leftInset = X(OverlayPixelPolicy.LeftInsetDip);
        var verticalInset = Y(OverlayPixelPolicy.VerticalInsetDip);
        var width = appearance.OverlayWidth;
        var height = appearance.OverlayHeight;
        var commands = new List<OverlayDrawCommand>
        {
            Rectangle(OverlayElementRole.Background, new LayoutRect(0, 0, width, height), BackgroundColor),
        };

        var minimumPaneWidth = X(20);
        var gaugePaneWidth = Math.Clamp(appearance.GaugePaneWidth, minimumPaneWidth, Math.Max(minimumPaneWidth, width - minimumPaneWidth));
        var stripeOffset = 0;
        var resolvedStripeColor = state.ServiceHealth switch
        {
            ServiceHealthState.Operational => (ColorValue?)null,
            ServiceHealthState.Issue => settings.Colors.ServiceIssue,
            ServiceHealthState.Unknown => settings.Colors.ServiceUnknown,
            _ => state.StatusStripeColor,
        };
        if (resolvedStripeColor is { } stripeColor && appearance.StatusStripeWidth > 0)
        {
            var stripeInset = Y(OverlayPixelPolicy.StatusVerticalInsetDip);
            var stripeBounds = new LayoutRect(leftInset, stripeInset, appearance.StatusStripeWidth, Math.Max(1, height - (stripeInset * 2)));
            commands.Add(Rectangle(OverlayElementRole.StatusStripe, stripeBounds, stripeColor));
            stripeOffset = appearance.StatusStripeWidth + X(OverlayPixelPolicy.StatusGapDip);
        }

        if (!string.IsNullOrWhiteSpace(state.ContentMessage))
        {
            commands.Add(new OverlayDrawCommand(
                OverlayDrawKind.Text,
                OverlayElementRole.ContentMessage,
                new LayoutRect(leftInset + stripeOffset, 0, Math.Max(1, width - (leftInset * 2) - stripeOffset), height),
                White,
                Text: state.ContentMessage,
                FontSize: Math.Clamp(height / 3, Y(OverlayPixelPolicy.MinimumMessageFontDip), Y(OverlayPixelPolicy.MaximumMessageFontDip))));
            if (isOverlayPositionChangeMode)
            {
                AddOverlayPositionOutline(commands, appearance);
            }

            return ApplyTheme(new UsageOverlayLayout(width, height, commands), settings.IsLight);
        }

        var gaugeLeft = leftInset + stripeOffset;
        var gaugeRight = gaugePaneWidth - X(OverlayPixelPolicy.GaugeRightInsetDip);
        var gaugeTop = verticalInset;
        var gaugeBottom = height - verticalInset;
        var gaugeHeight = Math.Max(1, gaugeBottom - gaugeTop);
        var quotaHeight = Math.Max(
            1,
            (gaugeHeight - (refreshHeight * 2) - gaugeGroupGap) / 2);
        var gaugeWidth = Math.Max(1, gaugeRight - gaugeLeft);
        var manaBounds = new LayoutRect(gaugeLeft, gaugeTop, gaugeWidth, quotaHeight);
        var manaRefreshBounds = new LayoutRect(
            gaugeLeft,
            manaBounds.Bottom,
            gaugeWidth,
            refreshHeight);
        var hpBounds = new LayoutRect(
            gaugeLeft,
            manaRefreshBounds.Bottom + gaugeGroupGap,
            gaugeWidth,
            quotaHeight);
        var hpRefreshTop = hpBounds.Bottom;
        var hpRefreshBounds = new LayoutRect(
            gaugeLeft,
            hpRefreshTop,
            gaugeWidth,
            Math.Max(1, Math.Min(refreshHeight, gaugeBottom - hpRefreshTop)));
        var fontSize = CalculateGaugeFontHeight(quotaHeight, appearance.DisplayScaleY);

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
        var separatorWidth = X(OverlayPixelPolicy.RefreshSeparatorDip);
        AddRefreshSeparators(commands, manaRefreshBounds, 5, separatorWidth, OverlayElementRole.ManaRefreshSeparator);
        AddRefreshSeparators(commands, hpRefreshBounds, 7, separatorWidth, OverlayElementRole.HpRefreshSeparator);

        AddGraph(commands, state.TokenBuckets, settings, height);

        if (isOverlayPositionChangeMode)
        {
            AddOverlayPositionOutline(commands, appearance);
        }

        return ApplyTheme(new UsageOverlayLayout(width, height, commands), settings.IsLight);
    }

    private static UsageOverlayLayout ApplyTheme(UsageOverlayLayout layout, bool isLight)
    {
        if (!isLight)
        {
            return layout;
        }

        var background = ColorValue.Parse("#F3F3F3");
        var text = ColorValue.Parse("#243042");
        var commands = new List<OverlayDrawCommand>();
        foreach (var command in layout.Commands)
        {
            var color = command.Role switch
            {
                OverlayElementRole.Background or OverlayElementRole.ManaRefreshSeparator
                    or OverlayElementRole.HpRefreshSeparator => background,
                OverlayElementRole.ManaTrack or OverlayElementRole.HpTrack => ColorValue.Parse("#D9DEE5"),
                OverlayElementRole.ManaRefreshTrack or OverlayElementRole.HpRefreshTrack => ColorValue.Parse("#CCD2D9"),
                OverlayElementRole.GraphGridDot => ColorValue.Parse("#B0B7C0"),
                OverlayElementRole.GraphBaseline => ColorValue.Parse("#536172"),
                OverlayElementRole.ContentMessage or OverlayElementRole.OverlayPositionOutline
                    or OverlayElementRole.ManaText or OverlayElementRole.HpText => text,
                _ => command.Color,
            };
            if (command.Role is OverlayElementRole.ManaText or OverlayElementRole.HpText)
            {
                var fillRole = command.Role == OverlayElementRole.ManaText
                    ? OverlayElementRole.ManaFill : OverlayElementRole.HpFill;
                var fill = layout.Commands.Single(item => item.Role == fillRole);
                // Keep one centered text layout, clipping each pass to its background.
                // This also keeps custom pale fills and stale gauges readable.
                var filledBackground = GdiUsageOverlayPainter.Blend(fill.Color, background, fill.Opacity);
                if (fill.Bounds.Width > 0)
                {
                    commands.Add(command with
                    {
                        Color = ReadableText(filledBackground),
                        Opacity = 1,
                        ClipBounds = fill.Bounds,
                    });
                }
                var remaining = command.Bounds with
                {
                    Left = fill.Bounds.Right,
                    Width = command.Bounds.Right - fill.Bounds.Right,
                };
                if (remaining.Width > 0)
                {
                    commands.Add(command with { Color = text, Opacity = 1, ClipBounds = remaining });
                }
                continue;
            }
            commands.Add(command with { Color = color });
        }
        return layout with { Commands = commands };
    }

    private static ColorValue ReadableText(ColorValue background)
    {
        static double Linear(byte channel)
        {
            var value = channel / 255.0;
            return value <= .04045 ? value / 12.92 : Math.Pow((value + .055) / 1.055, 2.4);
        }
        static double Luminance(ColorValue color) =>
            .2126 * Linear(color.Red) + .7152 * Linear(color.Green) + .0722 * Linear(color.Blue);
        var dark = ColorValue.Parse("#243042");
        var luminance = Luminance(background);
        return 1.05 / (luminance + .05) >= (luminance + .05) / (Luminance(dark) + .05)
            ? White : dark;
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
        var minimumHeight = (int)Math.Ceiling(OverlayPixelPolicy.MinimumGaugeFontDip * validDisplayScale);
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

    private static void AddRefreshSeparators(
        ICollection<OverlayDrawCommand> commands,
        LayoutRect bounds,
        int segmentCount,
        int preferredGapWidth,
        OverlayElementRole separatorRole)
    {
        // Keep at least one visible pixel per segment, even in a narrow gauge pane.
        if (bounds.Width < (segmentCount * 2) - 1)
        {
            return;
        }

        var gapWidth = Math.Min(preferredGapWidth, Math.Max(1, (bounds.Width / segmentCount) - 1));
        for (var segment = 1; segment < segmentCount; segment++)
        {
            var position = bounds.Width * segment / (double)segmentCount;
            var boundary = gapWidth == 1
                ? (int)Math.Floor(position)
                : (int)Math.Round(position, MidpointRounding.AwayFromZero);
            commands.Add(Rectangle(
                separatorRole,
                new LayoutRect(bounds.Left + boundary - (gapWidth / 2), bounds.Top, gapWidth, bounds.Height),
                BackgroundColor));
        }
    }

    private static void AddGraph(
        ICollection<OverlayDrawCommand> commands,
        IReadOnlyList<int> buckets,
        OverlayPresentationSettings settings,
        int overlayHeight)
    {
        var chartLeft = TokenGraphViewport.ChartLeft(settings.Appearance);
        var chartRight = TokenGraphViewport.ChartRight(settings.Appearance);
        var chartTop = OverlayPixelPolicy.ToPixels(OverlayPixelPolicy.ChartTopInsetDip, settings.Appearance.DisplayScaleY);
        var baselineTop = overlayHeight - OverlayPixelPolicy.GraphBaselinePixels
            - OverlayPixelPolicy.ToPixels(OverlayPixelPolicy.ChartBottomInsetDip, settings.Appearance.DisplayScaleY);
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

            for (var y = chartTop; y < chartBottom; y += OverlayPixelPolicy.GraphGridPeriodPixels)
            {
                commands.Add(Rectangle(
                    OverlayElementRole.GraphGridDot,
                    new LayoutRect(x, y, OverlayPixelPolicy.GraphGridWidthPixels, Math.Min(OverlayPixelPolicy.GraphGridDotPixels, chartBottom - y)),
                    GridColor));
            }
        }

        commands.Add(Rectangle(
            OverlayElementRole.GraphBaseline,
            new LayoutRect(chartLeft, baselineTop, chartRight - chartLeft, OverlayPixelPolicy.GraphBaselinePixels),
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
        EffectiveAppearanceSettings appearance)
    {
        var width = appearance.OverlayWidth;
        var height = appearance.OverlayHeight;
        var horizontal = Math.Min(width, OverlayPixelPolicy.ToPixels(OverlayPixelPolicy.PositionOutlineDip, appearance.DisplayScaleX));
        var vertical = Math.Min(height, OverlayPixelPolicy.ToPixels(OverlayPixelPolicy.PositionOutlineDip, appearance.DisplayScaleY));
        var middleHeight = Math.Max(0, height - (vertical * 2));
        commands.Add(Rectangle(OverlayElementRole.OverlayPositionOutline, new LayoutRect(0, 0, width, vertical), White));
        commands.Add(Rectangle(OverlayElementRole.OverlayPositionOutline, new LayoutRect(0, height - vertical, width, vertical), White));
        commands.Add(Rectangle(OverlayElementRole.OverlayPositionOutline, new LayoutRect(0, vertical, horizontal, middleHeight), White));
        commands.Add(Rectangle(OverlayElementRole.OverlayPositionOutline, new LayoutRect(width - horizontal, vertical, horizontal, middleHeight), White));
    }

    private static OverlayDrawCommand Rectangle(
        OverlayElementRole role,
        LayoutRect bounds,
        ColorValue color,
        double opacity = 1) =>
        new(OverlayDrawKind.Rectangle, role, bounds, color, opacity);
}
