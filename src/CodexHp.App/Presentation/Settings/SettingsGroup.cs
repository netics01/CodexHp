namespace CodexHp.App.Presentation.Settings;

public enum SettingsGroupKind
{
    General,
    Color,
    Appearance,
    OverlayPosition,
}

public sealed record SettingsGroup(SettingsGroupKind Kind, string Title);

public enum SettingsCloseReason
{
    Confirmed,
    Cancelled,
}

public enum SettingsCancelTrigger
{
    CancelButton,
    WindowClose,
    EscapeKey,
}

public sealed record SettingsCloseRequest(
    SettingsCloseReason Reason,
    SettingsCancelTrigger? CancelTrigger = null);
