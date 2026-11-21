using CodexHp.App.Application;
using CodexHp.Core.Positioning;
using CodexHp.Core.Settings;
using Xunit;

namespace CodexHp.App.Tests.Application;

public sealed class OverlayPlacementRecoveryTests
{
    private static readonly MonitorGeometry Monitor = new("DISPLAY", new(0, 0, 3840, 2160),
        new(0, 0, 3840, 2064), 2, 2, true, "stable-monitor");
    private static readonly PhysicalRect Taskbar = new(0, 2064, 3840, 96);
    private static readonly AppSettings Settings = AppSettings.Default with
    {
        Location = new OverlayLocationSettings("DISPLAY", 0, 2081, "stable-monitor"),
    };

    [Theory]
    [InlineData(false, "TaskbarUnavailable")]
    [InlineData(true, "TaskbarTooSmall")]
    public void Startup_fallback_is_retained_without_repeated_moves_then_recovers(bool shortTaskbar, string reason)
    {
        PhysicalRect? taskbar = shortTaskbar ? new(0, 2112, 3840, 48) : null;
        var applied = new List<OverlayDisplayResolution>();
        var logger = new RecordingLogger();
        var recovery = new OverlayPlacementRecovery(
            settings => OverlayDisplayResolver.Resolve(settings, [new(Monitor, taskbar)]),
            (_, resolution) => applied.Add(resolution), _ => null, logger);

        Assert.True(recovery.Update(Settings, allowFallback: true));
        var fallback = Assert.Single(applied).Placement;
        Assert.Equal(Monitor.WorkArea.Bottom - fallback.PhysicalHeight, fallback.PhysicalTop);
        for (var retry = 0; retry < 12; retry++)
        {
            Assert.True(recovery.Refresh(Settings, positionEditing: false));
        }
        Assert.Single(applied);
        Assert.Contains(reason, Assert.Single(logger.Messages));

        taskbar = Taskbar;
        Assert.False(recovery.Refresh(Settings, positionEditing: false));
        Assert.Equal(2, applied.Count);
        Assert.Equal(2081, applied[^1].Placement.PhysicalTop);
        Assert.Contains("Recovery completed", logger.Messages[^1]);
        Assert.Equal(OverlayPlacementTarget.Taskbar, Settings.Location.Target);
        Assert.Equal(2081, Settings.Location.Y);
    }

    [Theory]
    [InlineData("TaskbarHostingFailed:SetParent:5")]
    [InlineData("TaskbarBoundsMismatch")]
    public void Recovery_requires_actual_hosting_and_bounds_not_just_a_resolved_target(string failure)
    {
        string? verificationFailure = failure;
        var logger = new RecordingLogger();
        var attempts = 0;
        var recovery = new OverlayPlacementRecovery(Resolve,
            (_, _) => attempts++, _ => verificationFailure, logger);

        Assert.True(recovery.Update(Settings, allowFallback: true));
        Assert.True(recovery.Refresh(Settings, positionEditing: false));
        Assert.Single(logger.Messages);
        verificationFailure = null;
        Assert.False(recovery.Refresh(Settings, positionEditing: false));
        Assert.Equal(3, attempts);
        Assert.Contains("Recovery completed", logger.Messages[^1]);
    }

    [Fact]
    public void Position_editing_suspends_all_probes_and_moves_then_resumes()
    {
        var probes = 0;
        var moves = 0;
        var recovery = new OverlayPlacementRecovery(settings => { probes++; return Resolve(settings); },
            (_, _) => moves++, _ => null, new RecordingLogger());

        Assert.True(recovery.Refresh(Settings, positionEditing: true));
        Assert.Equal(0, probes);
        Assert.Equal(0, moves);
        Assert.False(recovery.Refresh(Settings, positionEditing: false));
        Assert.Equal(1, probes);
        Assert.Equal(1, moves);
    }

    [Fact]
    public void Desktop_choice_cancels_pending_recovery_and_is_not_verified_as_taskbar()
    {
        var logger = new RecordingLogger();
        var verificationCalls = 0;
        var recovery = new OverlayPlacementRecovery(Resolve, (_, _) => { },
            _ => { verificationCalls++; return "TaskbarNotAttached"; }, logger);
        Assert.True(recovery.Update(Settings, allowFallback: true));

        var desktop = Settings with { Location = Settings.Location with { Target = OverlayPlacementTarget.Desktop } };
        Assert.False(recovery.Update(desktop, allowFallback: true));
        Assert.False(recovery.Refresh(desktop, positionEditing: false));
        Assert.Equal(1, verificationCalls);
        Assert.Equal(2, logger.Messages.Count);
        Assert.Contains("Recovery cancelled", logger.Messages[^1]);
    }

    [Fact]
    public void Failed_refresh_retries_without_flooding_logs_and_reports_reason_changes()
    {
        var failure = "first failure";
        var logger = new RecordingLogger();
        var recovery = new OverlayPlacementRecovery(
            settings => failure.Length > 0 ? throw new InvalidOperationException(failure) : Resolve(settings),
            (_, _) => { }, _ => null, logger);

        Assert.True(recovery.Refresh(Settings, false));
        Assert.True(recovery.Refresh(Settings, false));
        Assert.Single(logger.Messages);
        failure = "different failure";
        Assert.True(recovery.Refresh(Settings, false));
        Assert.Equal(2, logger.Messages.Count);
        failure = "";
        Assert.False(recovery.Refresh(Settings, false));
        Assert.Contains("Recovery completed", logger.Messages[^1]);
        Assert.False(recovery.Refresh(Settings, false));
        Assert.Equal(3, logger.Messages.Count);
    }

    private static OverlayDisplayResolution Resolve(AppSettings settings) =>
        OverlayDisplayResolver.Resolve(settings, [new(Monitor, Taskbar)]);

    private sealed class RecordingLogger : IDiagnosticLogger
    {
        public List<string> Messages { get; } = [];
        public void Log(DiagnosticLevel level, string component, string message, Exception? exception = null) =>
            this.Messages.Add(message);
    }
}
