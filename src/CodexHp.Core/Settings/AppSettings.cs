namespace CodexHp.Core.Settings;

public enum OverlayColorMode
{
    System,
    Light,
    Dark,
}

public sealed record ColorSettings(
    ColorValue ManaBar,
    ColorValue HpBar,
    ColorValue RefreshGauge,
    ColorValue ServiceIssue,
    ColorValue ServiceUnknown,
    ColorValue TokenLow,
    ColorValue TokenHigh)
{
    public static ColorSettings LightDefault { get; } = new(
        ColorValue.Parse("#2368C4"), ColorValue.Parse("#BD3447"),
        ColorValue.Parse("#005A86"), ColorValue.Parse("#B66B00"),
        ColorValue.Parse("#687483"), ColorValue.Parse("#2368C4"), ColorValue.Parse("#BD3447"));

    public static ColorSettings Default { get; } = new(
        ManaBar: ColorValue.Parse("#3A8EFF"),
        HpBar: ColorValue.Parse("#DC4856"),
        RefreshGauge: ColorValue.Parse("#FFFFFF"),
        ServiceIssue: ColorValue.Parse("#F5A623"),
        ServiceUnknown: ColorValue.Parse("#808080"),
        TokenLow: ColorValue.Parse("#2667CD"),
        TokenHigh: ColorValue.Parse("#DC4856"));
}

public sealed record AppSettings(
    int SchemaVersion,
    bool StartWithWindows,
    bool ShowOnlyWhenChatGptRunning,
    ColorSettings Colors,
    AppearanceSettings Appearance,
    OverlayLocationSettings Location)
{
    public const int CurrentSchemaVersion = 4;

    public OverlayColorMode ColorMode { get; init; } = OverlayColorMode.System;

    // Keep the existing Colors profile as the dark palette for backward compatibility.
    public ColorSettings LightColors { get; init; } = ColorSettings.LightDefault;

    public bool UsesLightColors(bool systemIsLight) =>
        this.ColorMode == OverlayColorMode.Light
        || (this.ColorMode == OverlayColorMode.System && systemIsLight);

    public ColorSettings GetColors(bool systemIsLight) =>
        this.UsesLightColors(systemIsLight) ? this.LightColors : this.Colors;

    public static AppSettings Default { get; } = new(
        SchemaVersion: CurrentSchemaVersion,
        StartWithWindows: true,
        ShowOnlyWhenChatGptRunning: false,
        Colors: ColorSettings.Default,
        Appearance: AppearanceSettings.Default,
        Location: OverlayLocationSettings.Default);
}
