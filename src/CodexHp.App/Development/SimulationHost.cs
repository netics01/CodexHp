using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Threading;
using CodexHp.App.Application;
using CodexHp.App.Infrastructure;
using CodexHp.App.Presentation;
using CodexHp.App.Presentation.Settings;
using CodexHp.Core.Domain;
using CodexHp.Core.Positioning;
using CodexHp.Core.Settings;

namespace CodexHp.App.Development;

// Development composition root: deliberately has no credentials, HTTP, settings store,
// token-file scanner, startup registration, or production logger dependencies.
internal sealed class SimulationHost : IDisposable
{
    private readonly SimulationSession session;
    private readonly OverlayPositionController positions;
    private readonly UsageOverlayWindow overlay;
    private readonly Window controls;
    private readonly TrayIconController tray;
    private readonly DispatcherTimer timer;
    private readonly DisplayEnvironmentWatcher displayWatcher;
    private readonly TextBlock description = new() { TextWrapping = TextWrapping.Wrap, Margin = new(0, 4, 0, 16) };
    private readonly TextBlock clockLabel = new() { Margin = new(0, 8, 0, 12) };
    private readonly Action exit;
    private OverlayDisplayResolution resolution;
    private SettingsWindow? settingsWindow;
    private SettingsWindowViewModel? settingsViewModel;
    private bool disposed;

    internal Window ControlWindow => this.controls;

    internal SimulationHost(string preset, Action exit)
    {
        this.exit = exit;
        var monitors = new WindowsMonitorService();
        var taskbar = new TaskbarWindowLocator();
        this.positions = new(monitors, id => taskbar.FindForMonitor(id)?.TaskbarBounds);
        var now = DateTimeOffset.Now.ToUnixTimeMilliseconds();
        this.session = new(now - now % 60_000);
        this.session.Select(preset);
        this.session.Settings = this.session.Settings with
        {
            Appearance = DefaultAppearanceFactory.Create(this.session.Settings, value => this.positions.Resolve(value).Appearance),
        };
        this.resolution = this.positions.Resolve(this.session.Settings);
        this.overlay = new(new OverlayWindowHost(taskbar, monitors));
        this.controls = this.CreateControls();
        this.overlay.OpenSettingsRequested += (_, _) => this.ShowControls();
        this.overlay.OverlayPositionChanged += bounds =>
        {
            var location = this.positions.Capture(bounds);
            if (this.settingsViewModel is { } vm) vm.PreviewLocation(location);
            else this.session.Settings = this.session.Settings with { Location = location };
            this.Render(true);
        };
        this.overlay.DisplayEnvironmentChangeRequested += (_, _) => this.Render(true);
        this.tray = new(new WindowsTrayIconView(), this.ShowControls, exit, openUpdate: this.PreviewUpdateLink);
        this.tray.SetStatusMessage("Simulation — Developer tool (sample data)");
        this.timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        this.timer.Tick += this.OnTick;
        this.displayWatcher = new(Dispatcher.CurrentDispatcher, () => { this.Render(true); return true; });
    }

    internal void Start()
    {
        this.Render(true);
        this.overlay.Show();
        this.Render(true);
        this.controls.Show();
        this.ConstrainControls();
        this.timer.Start();
    }

    private Window CreateControls()
    {
        var panel = new StackPanel { Margin = new(24) };
        panel.Children.Add(new TextBlock { Text = "Simulation", FontSize = 24, FontWeight = FontWeights.SemiBold });
        panel.Children.Add(new TextBlock
        {
            Text = "DEVELOPER TOOL · SAMPLE DATA ONLY", FontSize = 11, Margin = new(0, 5, 0, 14),
            FontWeight = FontWeights.SemiBold,
        });
        panel.Children.Add(new TextBlock
        {
            Text = "For development and screenshots. No real account, network requests, saved settings, or Windows startup changes.",
            TextWrapping = TextWrapping.Wrap, Margin = new(0, 0, 0, 20),
        });
        panel.Children.Add(new TextBlock { Text = "Scenario", Margin = new(0, 0, 0, 5) });
        var presets = new ComboBox { ItemsSource = SimulationSession.Presets, SelectedItem = this.session.Preset, MinHeight = 32 };
        presets.SelectionChanged += (_, _) =>
        {
            if (this.settingsWindow is not null)
            {
                this.settingsViewModel?.Cancel(SettingsCancelTrigger.WindowClose);
            }
            this.session.Select(((SimulationPreset)presets.SelectedItem).Id);
            this.description.Text = this.session.Preset.Description;
            this.Render(true);
        };
        panel.Children.Add(presets);
        this.description.Text = this.session.Preset.Description;
        panel.Children.Add(this.description);
        var frozen = new CheckBox { Content = "Freeze simulated time", IsChecked = true };
        frozen.Checked += (_, _) => { this.session.Clock.SetFrozen(true); this.Render(); };
        frozen.Unchecked += (_, _) => { this.session.Clock.SetFrozen(false); this.Render(); };
        panel.Children.Add(frozen);
        panel.Children.Add(this.clockLabel);
        var timeButtons = new WrapPanel();
        AddButton(timeButtons, "+1 minute", () => { this.session.Clock.Advance(TimeSpan.FromMinutes(1)); this.Render(); });
        AddButton(timeButtons, "+1 hour", () => { this.session.Clock.Advance(TimeSpan.FromHours(1)); this.Render(); });
        AddButton(timeButtons, "+1 day", () => { this.session.Clock.Advance(TimeSpan.FromDays(1)); this.Render(); });
        AddButton(timeButtons, "Restart scenario", () => { this.session.Select(this.session.Preset.Id); this.Render(); });
        panel.Children.Add(timeButtons);
        panel.Children.Add(new TextBlock { Text = "Tooltip", Margin = new(0, 16, 0, 5) });
        var tooltip = new ComboBox { ItemsSource = Enum.GetValues<SimulationTooltipMode>(), SelectedIndex = 0, MinHeight = 32 };
        tooltip.SelectionChanged += (_, _) => this.overlay.SetSimulationTooltipMode((SimulationTooltipMode)tooltip.SelectedItem);
        panel.Children.Add(tooltip);
        panel.Children.Add(new TextBlock
        {
            Text = "Hover = normal behavior · Pinned = always shown · Hidden = no tooltip",
            FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new(0, 6, 0, 16),
        });
        var actions = new WrapPanel();
        AddButton(actions, "Sample appearance & position…", this.ShowSettings);
        AddButton(actions, "Hide controls for capture", () =>
        {
            this.settingsViewModel?.Cancel(SettingsCancelTrigger.WindowClose);
            this.controls.Hide();
        });
        panel.Children.Add(actions);
        panel.Children.Add(new TextBlock
        {
            Text = "Reopen controls from the tray icon or by double-clicking the overlay. Closing this window exits simulation. Restart without --simulate to return to live data.",
            TextWrapping = TextWrapping.Wrap, FontSize = 11, Margin = new(0, 12, 0, 0),
        });
        var window = new Window
        {
            Title = "CodexHp Simulation — Developer tool", Width = 540, Height = 640,
            MinWidth = 440, MinHeight = 400, WindowStartupLocation = WindowStartupLocation.CenterScreen,
            Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto },
        };
        window.Closed += (_, _) => { if (!this.disposed) this.exit(); };
        window.DpiChanged += (_, _) => window.Dispatcher.BeginInvoke(this.ConstrainControls);
        return window;
    }

    private static void AddButton(Panel panel, string label, Action action)
    {
        var button = new Button { Content = label, Padding = new(10, 6, 10, 6), Margin = new(0, 0, 8, 8) };
        button.Click += (_, _) => action();
        panel.Children.Add(button);
    }

    private void ShowControls()
    {
        this.controls.Show();
        if (this.controls.WindowState == WindowState.Minimized) this.controls.WindowState = WindowState.Normal;
        this.ConstrainControls();
        this.controls.Activate();
    }

    private void ConstrainControls()
    {
        if (this.disposed || !this.controls.IsVisible) return;
        var handle = new WindowInteropHelper(this.controls).Handle;
        var monitor = new WindowsMonitorService().GetMonitorForWindow(handle);
        if (monitor is null) return;
        var maxWidth = monitor.WorkArea.Width / monitor.ScaleX;
        var maxHeight = monitor.WorkArea.Height / monitor.ScaleY;
        this.controls.MinWidth = Math.Min(440, maxWidth);
        this.controls.MinHeight = Math.Min(400, maxHeight);
        this.controls.MaxWidth = maxWidth;
        this.controls.MaxHeight = maxHeight;
        if (!NativeMethods.GetWindowRect(handle, out var rect)) return;
        var width = Math.Min(rect.Right - rect.Left, monitor.WorkArea.Width);
        var height = Math.Min(rect.Bottom - rect.Top, monitor.WorkArea.Height);
        NativeMethods.SetWindowPos(handle, NativeMethods.HwndTop,
            Math.Clamp(rect.Left, monitor.WorkArea.Left, monitor.WorkArea.Right - width),
            Math.Clamp(rect.Top, monitor.WorkArea.Top, monitor.WorkArea.Bottom - height), width, height,
            NativeMethods.SwpNoActivate | NativeMethods.SwpNoZOrder);
    }

    private void ShowSettings()
    {
        if (this.settingsWindow is not null) { this.settingsWindow.Activate(); return; }
        void Preview(AppSettings settings)
        {
            this.session.Settings = settings with { StartWithWindows = false, ShowOnlyWhenChatGptRunning = false };
            this.Render(true);
        }
        var vm = new SettingsWindowViewModel(this.session.Settings, Preview, this.overlay.SetOverlayPositionChangeMode,
            settings => { Preview(settings); return this.session.Settings; }, canStartWithWindows: false,
            calculateVisibleTokenHistory: settings => TokenGraphViewport.CalculateVisibleDuration(this.positions.Resolve(settings).Appearance),
            resolveDefaultAppearance: settings => DefaultAppearanceFactory.Create(settings with { Appearance = AppearanceSettings.Default },
                value => this.positions.Resolve(value).Appearance),
            systemUsesLightColors: () => WindowsShellTheme.Read() == TrayIconTheme.Light,
            openUpdate: this.PreviewUpdateLink)
        { ApplicationTitleText = "CodexHp-Dev · Simulation", CanChooseProcessVisibility = false };
        this.settingsViewModel = vm;
        vm.SetAvailableUpdate(this.session.AvailableUpdate);
        var window = new SettingsWindow(vm) { Title = "Simulation settings — Temporary sample only", Owner = this.controls };
        this.settingsWindow = window;
        window.Closed += (_, _) => { this.settingsWindow = null; this.settingsViewModel = null; };
        window.Show();
        var display = this.positions.GetDisplays().First(d => d.Monitor.Id == this.resolution.Placement.MonitorId);
        window.ConstrainToWorkArea(display.Monitor, center: true);
    }

    private void OnTick(object? sender, EventArgs args) => this.Render();

    private void PreviewUpdateLink(AvailableUpdate update) => MessageBox.Show(
        $"Simulation only — the browser was not opened.\n\nRelease link target:\n{update.ReleaseUri}",
        "Simulation — Update link", MessageBoxButton.OK, MessageBoxImage.Information);

    private void Render(bool resolvePlacement = false)
    {
        this.tray.SetAvailableUpdate(this.session.AvailableUpdate);
        this.settingsViewModel?.SetAvailableUpdate(this.session.AvailableUpdate);
        if (this.disposed) return;
        this.tray.SetAvailableUpdate(this.session.AvailableUpdate);
        this.settingsViewModel?.SetAvailableUpdate(this.session.AvailableUpdate);
        if (resolvePlacement)
        {
            this.resolution = this.positions.Resolve(this.session.Settings);
            this.ConstrainControls();
        }
        var light = this.session.Settings.UsesLightColors(WindowsShellTheme.Read() == TrayIconTheme.Light);
        var state = this.session.CreateState(TokenGraphViewport.CalculateVisibleBucketCount(this.resolution.Appearance));
        this.overlay.Apply(state, new OverlayPresentationSettings(this.session.Settings.GetColors(light), this.resolution.Appearance, light));
        if (resolvePlacement && !this.overlay.IsOverlayPositionChangeMode) this.overlay.SetPlacement(this.resolution.Placement);
        this.clockLabel.Text = $"Simulated local time: {DateTimeOffset.FromUnixTimeMilliseconds(this.session.Clock.Now).ToLocalTime():yyyy-MM-dd HH:mm:ss}";
    }

    public void Dispose()
    {
        if (this.disposed) return;
        this.disposed = true;
        this.timer.Stop();
        this.timer.Tick -= this.OnTick;
        this.displayWatcher.Dispose();
        this.settingsViewModel?.Cancel(SettingsCancelTrigger.WindowClose);
        this.overlay.CloseForShutdown();
        this.tray.Dispose();
        this.controls.Close();
    }
}
