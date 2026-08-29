using System.ComponentModel;
using System.Reflection;
using System.Runtime.CompilerServices;
using CodexHp.Core.Domain;
using CodexHp.Core.Settings;

namespace CodexHp.App.Presentation.Settings;

public sealed class SettingsWindowViewModel : INotifyPropertyChanged
{
    private static readonly ApplicationBuildInfo CurrentBuild = ResolveApplicationBuild();
    private readonly AppSettings baseline;
    private readonly SettingsEditSession editSession;
    private readonly Action<AppSettings> preview;
    private readonly Action<bool> changeOverlayPositionMode;
    private readonly Func<AppSettings, AppSettings> commit;
    private readonly bool canEnableStartWithWindows;
    private SettingsGroup selectedGroup;

    public SettingsWindowViewModel(
        AppSettings baseline,
        Action<AppSettings>? preview,
        Action<bool>? changeOverlayPositionMode,
        Func<AppSettings, AppSettings> commit,
        bool canStartWithWindows = true)
    {
        this.baseline = baseline ?? throw new ArgumentNullException(nameof(baseline));
        this.preview = preview ?? (_ => { });
        this.changeOverlayPositionMode = changeOverlayPositionMode ?? (_ => { });
        this.commit = commit ?? throw new ArgumentNullException(nameof(commit));
        this.canEnableStartWithWindows = canStartWithWindows;
        this.editSession = new SettingsEditSession(baseline);
        this.Groups =
        [
            new SettingsGroup(SettingsGroupKind.General, "General"),
            new SettingsGroup(SettingsGroupKind.Color, "Colors"),
            new SettingsGroup(SettingsGroupKind.Appearance, "Appearance"),
            new SettingsGroup(SettingsGroupKind.OverlayPosition, "Overlay Position"),
            new SettingsGroup(SettingsGroupKind.About, "About"),
        ];
        this.selectedGroup = this.Groups[0];
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public event Action<SettingsCloseRequest>? CloseRequested;

    public IReadOnlyList<SettingsGroup> Groups { get; }

    public string ApplicationVersionText { get; } = $"Version {CurrentBuild.Version}";

    public string ApplicationCommitText { get; } = $"Commit {CurrentBuild.CommitHash}";

    public AppSettings Working => this.editSession.Working;

    public bool CanStartWithWindows => this.canEnableStartWithWindows || this.StartWithWindows;

    public bool IsClosed { get; private set; }

    public SettingsGroup SelectedGroup
    {
        get => this.selectedGroup;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            if (this.selectedGroup == value)
            {
                return;
            }

            var wasOverlayPosition = this.selectedGroup.Kind == SettingsGroupKind.OverlayPosition;
            this.selectedGroup = value;
            this.OnPropertyChanged();
            var isOverlayPosition = value.Kind == SettingsGroupKind.OverlayPosition;
            if (wasOverlayPosition != isOverlayPosition)
            {
                this.changeOverlayPositionMode(isOverlayPosition);
            }
        }
    }

    public bool StartWithWindows
    {
        get => this.Working.StartWithWindows;
        set
        {
            if (value && !this.canEnableStartWithWindows && !this.Working.StartWithWindows)
            {
                return;
            }

            this.UpdateWorking(this.Working with { StartWithWindows = value }, previewVisual: false);
        }
    }

    public bool ShowOnlyWhenChatGptRunning
    {
        get => this.Working.ShowOnlyWhenChatGptRunning;
        set => this.UpdateWorking(this.Working with { ShowOnlyWhenChatGptRunning = value }, previewVisual: false);
    }

    public ColorValue ManaBarColor
    {
        get => this.Working.Colors.ManaBar;
        set => this.UpdateColors(this.Working.Colors with { ManaBar = value });
    }

    public ColorValue HpBarColor
    {
        get => this.Working.Colors.HpBar;
        set => this.UpdateColors(this.Working.Colors with { HpBar = value });
    }

    public ColorValue RefreshGaugeColor
    {
        get => this.Working.Colors.RefreshGauge;
        set => this.UpdateColors(this.Working.Colors with { RefreshGauge = value });
    }

    public ColorValue ServiceIssueColor
    {
        get => this.Working.Colors.ServiceIssue;
        set => this.UpdateColors(this.Working.Colors with { ServiceIssue = value });
    }

    public ColorValue ServiceUnknownColor
    {
        get => this.Working.Colors.ServiceUnknown;
        set => this.UpdateColors(this.Working.Colors with { ServiceUnknown = value });
    }

    public ColorValue TokenLowColor
    {
        get => this.Working.Colors.TokenLow;
        set => this.UpdateColors(this.Working.Colors with { TokenLow = value });
    }

    public ColorValue TokenHighColor
    {
        get => this.Working.Colors.TokenHigh;
        set => this.UpdateColors(this.Working.Colors with { TokenHigh = value });
    }

    public void ResetColorsToDefaults() =>
        this.UpdateColors(ColorSettings.Default);

    public int OverlayWidth
    {
        get => this.Working.Appearance.OverlayWidth;
        set => this.UpdateAppearance(this.Working.Appearance with { OverlayWidth = value });
    }

    public int OverlayHeight
    {
        get => this.Working.Appearance.OverlayHeight;
        set => this.UpdateAppearance(this.Working.Appearance with { OverlayHeight = value });
    }

    public int GaugePaneWidth
    {
        get => this.Working.Appearance.GaugePaneWidth;
        set => this.UpdateAppearance(this.Working.Appearance with { GaugePaneWidth = value });
    }

    public int GraphBarWidth
    {
        get => this.Working.Appearance.GraphBarWidth;
        set => this.UpdateAppearance(this.Working.Appearance with { GraphBarWidth = value });
    }

    public int GraphBarGap
    {
        get => this.Working.Appearance.GraphBarGap;
        set => this.UpdateAppearance(this.Working.Appearance with { GraphBarGap = value });
    }

    public int StatusStripeWidth
    {
        get => this.Working.Appearance.StatusStripeWidth;
        set => this.UpdateAppearance(this.Working.Appearance with { StatusStripeWidth = value });
    }

    public string VisibleTokenHistoryText
    {
        get
        {
            var duration = TokenGraphViewport.CalculateVisibleDuration(this.Working.Appearance);
            return $"Visible token history: {(int)duration.TotalMinutes} min {duration.Seconds} sec";
        }
    }

    public void ResetAppearanceToDefaults() =>
        this.UpdateAppearance(AppearanceSettings.Default);

    public void PreviewLocation(OverlayLocationSettings location)
    {
        ArgumentNullException.ThrowIfNull(location);
        this.UpdateWorking(this.Working with { Location = location }, previewVisual: true);
    }

    public void Confirm()
    {
        this.ThrowIfClosed();
        var desired = SettingsValidator.Validate(this.Working).Settings;
        var committed = this.commit(desired);
        this.editSession.Preview(committed);
        this.editSession.Confirm();
        this.preview(committed);
        this.ExitOverlayPositionMode();
        this.IsClosed = true;
        this.CloseRequested?.Invoke(new SettingsCloseRequest(SettingsCloseReason.Confirmed));
    }

    public void Cancel(SettingsCancelTrigger trigger = SettingsCancelTrigger.CancelButton)
    {
        if (this.IsClosed)
        {
            return;
        }

        var restored = this.editSession.Cancel();
        this.preview(restored);
        this.ExitOverlayPositionMode();
        this.IsClosed = true;
        this.CloseRequested?.Invoke(new SettingsCloseRequest(SettingsCloseReason.Cancelled, trigger));
    }

    private void UpdateColors(ColorSettings colors) =>
        this.UpdateWorking(this.Working with { Colors = colors }, previewVisual: true);

    private void UpdateAppearance(AppearanceSettings appearance)
    {
        var candidate = this.Working with { Appearance = appearance };
        var validated = SettingsValidator.Validate(candidate).Settings;
        this.UpdateWorking(validated, previewVisual: true);
    }

    private void UpdateWorking(AppSettings settings, bool previewVisual)
    {
        this.ThrowIfClosed();
        if (settings == this.Working)
        {
            return;
        }

        this.editSession.Preview(settings);
        this.OnPropertyChanged(string.Empty);
        if (previewVisual)
        {
            this.preview(settings with
            {
                StartWithWindows = this.baseline.StartWithWindows,
                ShowOnlyWhenChatGptRunning = this.baseline.ShowOnlyWhenChatGptRunning,
            });
        }
    }

    private void ExitOverlayPositionMode()
    {
        if (this.selectedGroup.Kind == SettingsGroupKind.OverlayPosition)
        {
            this.changeOverlayPositionMode(false);
        }
    }

    private void ThrowIfClosed()
    {
        if (this.IsClosed)
        {
            throw new InvalidOperationException("The settings edit session is already closed.");
        }
    }

    private static ApplicationBuildInfo ResolveApplicationBuild()
    {
        var assembly = typeof(SettingsWindowViewModel).Assembly;
        var informationalVersion = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;
        var assemblyVersion = assembly.GetName().Version?.ToString(3) ?? "Unknown";
        if (string.IsNullOrWhiteSpace(informationalVersion))
        {
            return new ApplicationBuildInfo(assemblyVersion, "Unknown");
        }

        var separator = informationalVersion.IndexOf('+', StringComparison.Ordinal);
        if (separator <= 0 || separator == informationalVersion.Length - 1)
        {
            return new ApplicationBuildInfo(informationalVersion, "Unknown");
        }

        return new ApplicationBuildInfo(
            informationalVersion[..separator],
            informationalVersion[(separator + 1)..]);
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    private sealed record ApplicationBuildInfo(string Version, string CommitHash);
}

public sealed class SettingsWindowController
{
    private readonly Func<SettingsWindowViewModel> createViewModel;
    private readonly Action<SettingsWindowViewModel> show;
    private readonly Action<SettingsWindowViewModel> activate;
    private SettingsWindowViewModel? current;

    public SettingsWindowController(
        Func<SettingsWindowViewModel> createViewModel,
        Action<SettingsWindowViewModel> show,
        Action<SettingsWindowViewModel> activate)
    {
        this.createViewModel = createViewModel ?? throw new ArgumentNullException(nameof(createViewModel));
        this.show = show ?? throw new ArgumentNullException(nameof(show));
        this.activate = activate ?? throw new ArgumentNullException(nameof(activate));
    }

    public SettingsWindowViewModel Open()
    {
        if (this.current is not null)
        {
            this.activate(this.current);
            return this.current;
        }

        this.current = this.createViewModel();
        this.current.CloseRequested += this.OnClosed;
        this.show(this.current);
        return this.current;
    }

    public SettingsWindowViewModel? Current => this.current;

    private void OnClosed(SettingsCloseRequest request)
    {
        if (this.current is null)
        {
            return;
        }

        this.current.CloseRequested -= this.OnClosed;
        this.current = null;
    }
}
