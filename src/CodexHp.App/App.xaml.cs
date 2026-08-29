using System.Net.Http;
using CodexHp.App.Application;
using CodexHp.App.Infrastructure;
using CodexHp.App.Presentation;
using CodexHp.App.Presentation.Settings;
using CodexHp.Core.Domain;
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
    private AppSettings activeSettings = AppSettings.Default;
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
        var settingsStore = new JsonSettingsStore();
        var startupRegistration = new StartupRegistration(
            Environment.ProcessPath ?? throw new InvalidOperationException("The executable path is not available."));
        this.activeSettings = settingsStore.Load() with
        {
            StartWithWindows = startupRegistration.IsEnabled(),
        };
        var settingsCommitService = new SettingsCommitService(settingsStore, startupRegistration);
        var monitorService = new WindowsMonitorService();
        this.positionController = new OverlayPositionController(monitorService);

        this.usageOverlayWindow = new UsageOverlayWindow(
            new OverlayWindowHost(new TaskbarWindowLocator(), monitorService));
        this.usageOverlayWindow.Apply(this.currentUsageOverlayState, this.activeSettings);
        this.usageOverlayWindow.SetPlacement(this.positionController.Restore(this.activeSettings));
        this.usageOverlayWindow.OpenSettingsRequested += (_, _) => this.OpenSettings();
        this.usageOverlayWindow.OverlayPositionChanged += this.OnOverlayPositionChanged;
        this.usageOverlayWindow.Show();

        this.settingsWindowController = new SettingsWindowController(
            () => new SettingsWindowViewModel(
                this.activeSettings,
                this.ApplySettingsPreview,
                enabled => this.usageOverlayWindow.SetOverlayPositionChangeMode(enabled),
                desired => settingsCommitService.Commit(desired),
                canStartWithWindows: startupRegistration.CanEnable),
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
            this.logger);
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
            this.usageOverlayWindow.Apply(state, this.activeSettings);
        });
    }

    private void ApplySettingsPreview(AppSettings settings)
    {
        this.activeSettings = settings;
        if (this.usageOverlayWindow is null || this.positionController is null)
        {
            return;
        }

        this.usageOverlayWindow.Apply(this.currentUsageOverlayState, settings);
        this.usageOverlayWindow.SetPlacement(this.positionController.Restore(settings));
    }

    private void OnOverlayPositionChanged(CodexHp.Core.Positioning.PhysicalRect overlayBounds)
    {
        if (this.positionController is null || this.settingsWindowController?.Current is not { } viewModel)
        {
            return;
        }

        viewModel.PreviewLocation(this.positionController.Capture(overlayBounds));
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
        this.usageOverlayWindow?.CloseForShutdown();
        this.usageOverlayWindow = null;
        this.httpClient?.Dispose();
        this.httpClient = null;
        this.singleInstance?.Dispose();
        this.singleInstance = null;
    }
}
