namespace CodexHp.Core.Settings;

public sealed record ColorSettings(
    ColorValue ManaBar,
    ColorValue HpBar,
    ColorValue RefreshGauge,
    ColorValue ServiceIssue,
    ColorValue ServiceUnknown,
    ColorValue TokenLow,
    ColorValue TokenHigh)
{
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

    public static AppSettings Default { get; } = new(
        SchemaVersion: CurrentSchemaVersion,
        StartWithWindows: true,
        ShowOnlyWhenChatGptRunning: false,
        Colors: ColorSettings.Default,
        Appearance: AppearanceSettings.Default,
        Location: OverlayLocationSettings.Default);
}
