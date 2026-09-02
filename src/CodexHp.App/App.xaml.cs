using System.Net.Http;
using CodexHp.App.Application;
using CodexHp.App.Infrastructure;
using CodexHp.App.Presentation;
using CodexHp.App.Presentation.Settings;
using CodexHp.Core.Domain;
using CodexHp.Core.Positioning;
using CodexHp.Core.Settings;

namespace CodexHp.App;

public partial class App : System.Windows.Application
{
    private SingleInstanceGuard? singleInstance;
    private RollingFileLogger? logger;
    private HttpClient? httpClient;
    private CancellationTokenSource? lifetimeCancellation;
    private Task? coordinatorTask;
    private TrayIconController? trayIcon;
    private UsageOverlayWindow? usageOverlayWindow;
    private SettingsWindow? settingsWindow;
    private SettingsWindowController? settingsWindowController;
    private OverlayPositionController? positionController;
    private DisplayEnvironmentWatcher? displayEnvironmentWatcher;
    private AppSettings activeSettings = AppSettings.Default;
    private OverlayPresentationSettings activePresentation =
        OverlayPresentationSettings.FromUnscaled(AppSettings.Default);
    private UsageOverlayState currentUsageOverlayState = UsageOverlayStateReducer.Reduce(
        UsageProviderState.Waiting,
        TokenActivityProviderState.Waiting,
        ServiceHealthState.Unknown,
        string.Empty,
        new VisibilityState(false, false),
        AppSettings.Default,
        DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
    private int shutdownStarted;

    protected override void OnStartup(System.Windows.StartupEventArgs eventArgs)
    {
        base.OnStartup(eventArgs);
        this.singleInstance = SingleInstanceGuard.TryAcquire();
        if (this.singleInstance is null)
        {
            this.Shutdown();
            return;
        }

        try
        {
            this.StartApplication();
        }
        catch (Exception exception)
        {
            this.logger?.Log(DiagnosticLevel.Error, "Startup", "CodexHp could not start.", exception);
            System.Windows.MessageBox.Show(
                $"{UserInterfaceText.StartupFailure}\n\n{exception.Message}",
                "CodexHp",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Error);
            this.DisposeResources();
            this.Shutdown();
        }
    }

    protected override void OnSessionEnding(System.Windows.SessionEndingCancelEventArgs eventArgs)
    {
        this.BeginShutdown();
        base.OnSessionEnding(eventArgs);
    }

    protected override void OnExit(System.Windows.ExitEventArgs eventArgs)
    {
        this.DisposeResources();
        base.OnExit(eventArgs);
    }

    private void StartApplication()
    {
        this.logger = new RollingFileLogger();
        var monitorService = new WindowsMonitorService();
        var taskbarLocator = new TaskbarWindowLocator();
        Func<string, PhysicalRect?> taskbarBounds = monitorId =>
            taskbarLocator.FindForMonitor(monitorId)?.TaskbarBounds;
        var settingsStore = new JsonSettingsStore(
            monitors: monitorService.GetMonitors,
            taskbarBounds: taskbarBounds);
        var startupRegistration = new StartupRegistration(
            Environment.ProcessPath ?? throw new InvalidOperationException("The executable path is not available."));
        this.activeSettings = settingsStore.Load() with
        {
            StartWithWindows = startupRegistration.IsEnabled(),
        };
        var settingsCommitService = new SettingsCommitService(settingsStore, startupRegistration);
        this.positionController = new OverlayPositionController(monitorService, taskbarBounds);
        var displayResolution = this.positionController.Resolve(this.activeSettings);
        this.activePresentation = new OverlayPresentationSettings(
            this.activeSettings.Colors,
            displayResolution.Appearance);

        this.usageOverlayWindow = new UsageOverlayWindow(
            new OverlayWindowHost(taskbarLocator, monitorService));
        this.usageOverlayWindow.Apply(this.currentUsageOverlayState, this.activePresentation);
        this.usageOverlayWindow.SetPlacement(displayResolution.Placement);
        this.usageOverlayWindow.OpenSettingsRequested += (_, _) => this.OpenSettings();
        this.usageOverlayWindow.OverlayPositionChanged += this.OnOverlayPositionChanged;
        this.usageOverlayWindow.DisplayEnvironmentChangeRequested +=
            (_, _) => this.displayEnvironmentWatcher?.RequestRefresh();
        this.usageOverlayWindow.Show();
        this.displayEnvironmentWatcher = new DisplayEnvironmentWatcher(
            this.Dispatcher,
            this.RefreshDisplayEnvironment);

        this.settingsWindowController = new SettingsWindowController(
            () => new SettingsWindowViewModel(
                this.activeSettings,
                this.ApplySettingsPreview,
                enabled => this.usageOverlayWindow.SetOverlayPositionChangeMode(enabled),
                desired => settingsCommitService.Commit(desired),
                canStartWithWindows: startupRegistration.CanEnable,
                calculateVisibleTokenHistory: this.CalculateVisibleTokenHistory,
                resolveDefaultAppearance: this.ResolveDefaultAppearance),
            this.ShowSettingsWindow,
            this.ActivateSettingsWindow);

        this.trayIcon = new TrayIconController(this.OpenSettings, this.BeginShutdown);
        this.httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(10),
        };
        this.lifetimeCancellation = new CancellationTokenSource();
        var clock = new SystemClock();
        var serviceStatusClient = new OpenAiServiceStatusClient(this.httpClient);
        var serviceStatusPoller = new OpenAiServiceStatusPoller(
            serviceStatusClient.FetchAsync,
            () => clock.UnixTimeMilliseconds);
        var visibilitySource = new WindowsVisibilitySource(
            new ChatGptProcessDetector(),
            new FullscreenDetector(),
            monitorService);
        var coordinator = new ApplicationCoordinator(
            new CodexCredentialSource(),
            new OpenAiUsageClient(this.httpClient),
            new CodexTokenUsageScanner(),
            serviceStatusPoller.ReadAsync,
            () => visibilitySource.Read(this.usageOverlayWindow.WindowHandle),
            () => Volatile.Read(ref this.activeSettings),
            clock,
            this.logger,
            readGraphAppearance: () => Volatile.Read(ref this.activePresentation).Appearance);
        coordinator.UsageOverlayStateChanged += this.OnUsageOverlayStateChanged;
        this.coordinatorTask = this.RunCoordinatorAsync(coordinator, this.lifetimeCancellation.Token);
        this.logger.Log(DiagnosticLevel.Information, "Lifecycle", "CodexHp started.");
    }

    private async Task RunCoordinatorAsync(
        ApplicationCoordinator coordinator,
        CancellationToken cancellationToken)
    {
        try
        {
            await coordinator.RunAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            this.logger?.Log(DiagnosticLevel.Error, "Coordinator", "The coordinator stopped unexpectedly.", exception);
            _ = this.Dispatcher.BeginInvoke(this.BeginShutdown);
        }
    }

    private void OnUsageOverlayStateChanged(UsageOverlayState state)
    {
        this.Dispatcher.BeginInvoke(() =>
        {
            if (Volatile.Read(ref this.shutdownStarted) != 0 || this.usageOverlayWindow is null)
            {
                return;
            }

            this.currentUsageOverlayState = state;
            this.usageOverlayWindow.Apply(state, this.activePresentation);
        });
    }

    private void ApplySettingsPreview(AppSettings settings)
    {
        this.activeSettings = settings;
        if (this.usageOverlayWindow is null || this.positionController is null)
        {
            return;
        }

        var displayResolution = this.positionController.Resolve(settings);
        this.activePresentation = new OverlayPresentationSettings(
            settings.Colors,
            displayResolution.Appearance);
        this.usageOverlayWindow.Apply(this.currentUsageOverlayState, this.activePresentation);
        this.usageOverlayWindow.SetPlacement(displayResolution.Placement);
    }

    private TimeSpan CalculateVisibleTokenHistory(AppSettings settings)
    {
        var appearance = this.positionController?.Resolve(settings).Appearance;
        return appearance is null
            ? TokenGraphViewport.CalculateVisibleDuration(settings.Appearance)
            : TokenGraphViewport.CalculateVisibleDuration(appearance);
    }

    private AppearanceSettings ResolveDefaultAppearance(AppSettings settings)
    {
        if (this.positionController is null)
        {
            return AppearanceSettings.Default;
        }

        var template = settings with { Appearance = AppearanceSettings.Default };
        return DefaultAppearanceFactory.Create(
            template,
            candidate => this.positionController.Resolve(candidate).Appearance);
    }

    private void OnOverlayPositionChanged(CodexHp.Core.Positioning.PhysicalRect overlayBounds)
    {
        if (this.positionController is null || this.settingsWindowController?.Current is not { } viewModel)
        {
            return;
        }

        viewModel.PreviewLocation(this.positionController.Capture(overlayBounds));
    }

    private bool RefreshDisplayEnvironment()
    {
        if (Volatile.Read(ref this.shutdownStarted) != 0
            || this.usageOverlayWindow is null
            || this.positionController is null)
        {
            return false;
        }

        try
        {
            var resolution = this.positionController.Resolve(this.activeSettings);
            if (resolution.TaskbarWasUnavailable)
            {
                this.logger?.Log(
                    DiagnosticLevel.Information,
                    "Display",
                    "The taskbar was unavailable during a display refresh; keeping the current overlay placement and retrying.");
                return true;
            }

            this.activePresentation = new OverlayPresentationSettings(
                this.activeSettings.Colors,
                resolution.Appearance);
            this.usageOverlayWindow.Apply(this.currentUsageOverlayState, this.activePresentation);
            this.usageOverlayWindow.SetPlacement(resolution.Placement);
            this.ConstrainSettingsWindow(resolution.Placement.MonitorId, center: false);
            return false;
        }
        catch (Exception exception)
        {
            this.logger?.Log(
                DiagnosticLevel.Warning,
                "Display",
                "The display environment could not be refreshed.",
                exception);
            return false;
        }
    }

    private void OpenSettings()
    {
        if (!this.Dispatcher.CheckAccess())
        {
            this.Dispatcher.BeginInvoke(this.OpenSettings);
            return;
        }

        this.settingsWindowController?.Open();
    }

    private void ShowSettingsWindow(SettingsWindowViewModel viewModel)
    {
        var window = new SettingsWindow(viewModel);
        this.settingsWindow = window;
        window.Closed += (_, _) =>
        {
            if (ReferenceEquals(this.settingsWindow, window))
            {
                this.settingsWindow = null;
            }
        };
        window.Show();
        if (this.positionController is { } controller)
        {
            var resolution = controller.Resolve(this.activeSettings);
            this.ConstrainSettingsWindow(resolution.Placement.MonitorId, center: true);
        }
    }

    private void ConstrainSettingsWindow(string monitorId, bool center)
    {
        if (this.settingsWindow is null || this.positionController is null)
        {
            return;
        }

        var monitor = this.positionController.GetDisplays()
            .Select(display => display.Monitor)
            .FirstOrDefault(candidate => string.Equals(
                candidate.Id,
                monitorId,
                StringComparison.OrdinalIgnoreCase));
        if (monitor is not null)
        {
            this.settingsWindow.ConstrainToWorkArea(monitor, center);
        }
    }

    private void ActivateSettingsWindow(SettingsWindowViewModel viewModel)
    {
        if (this.settingsWindow is null)
        {
            return;
        }

        if (this.settingsWindow.WindowState == System.Windows.WindowState.Minimized)
        {
            this.settingsWindow.WindowState = System.Windows.WindowState.Normal;
        }

        this.settingsWindow.Activate();
    }

    private void BeginShutdown()
    {
        if (!this.Dispatcher.CheckAccess())
        {
            this.Dispatcher.BeginInvoke(this.BeginShutdown);
            return;
        }

        if (Interlocked.Exchange(ref this.shutdownStarted, 1) != 0)
        {
            return;
        }

        _ = this.ShutdownAsync();
    }

    private async Task ShutdownAsync()
    {
        this.trayIcon?.Dispose();
        this.trayIcon = null;
        this.lifetimeCancellation?.Cancel();
        if (this.coordinatorTask is not null)
        {
            await this.coordinatorTask;
        }

        this.settingsWindowController?.Current?.Cancel(SettingsCancelTrigger.WindowClose);
        this.usageOverlayWindow?.CloseForShutdown();
        this.logger?.Log(DiagnosticLevel.Information, "Lifecycle", "CodexHp stopped.");
        this.DisposeResources();
        this.Shutdown();
    }

    private void DisposeResources()
    {
        this.lifetimeCancellation?.Cancel();
        this.lifetimeCancellation?.Dispose();
        this.lifetimeCancellation = null;
        this.trayIcon?.Dispose();
        this.trayIcon = null;
        this.displayEnvironmentWatcher?.Dispose();
        this.displayEnvironmentWatcher = null;
        this.usageOverlayWindow?.CloseForShutdown();
        this.usageOverlayWindow = null;
        this.httpClient?.Dispose();
        this.httpClient = null;
        this.singleInstance?.Dispose();
        this.singleInstance = null;
    }
}
