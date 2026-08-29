using CodexHp.App.Presentation.Settings;
using CodexHp.Core.Settings;
using Xunit;

namespace CodexHp.App.Tests.Presentation;

public sealed class SettingsWindowViewModelTests
{
    [Fact]
    public void Groups_are_ordered_general_color_appearance_position_and_about()
    {
        var viewModel = CreateViewModel();

        Assert.Equal(
            [SettingsGroupKind.General, SettingsGroupKind.Color, SettingsGroupKind.Appearance, SettingsGroupKind.OverlayPosition, SettingsGroupKind.About],
            viewModel.Groups.Select(group => group.Kind));
        Assert.Equal(["General", "Colors", "Appearance", "Overlay Position", "About"], viewModel.Groups.Select(group => group.Title));
    }

    [Fact]
    public void Color_appearance_and_location_changes_publish_immediate_preview()
    {
        var previews = new List<AppSettings>();
        var viewModel = CreateViewModel(previews: previews);

        viewModel.ManaBarColor = ColorValue.Parse("#010203");
        viewModel.OverlayWidth = 420;
        viewModel.PreviewLocation(new OverlayLocationSettings("DISPLAY2", 10, 20));

        Assert.Equal(3, previews.Count);
        Assert.Equal("#010203", previews[0].Colors.ManaBar.ToHex());
        Assert.Equal(420, previews[1].Appearance.OverlayWidth);
        Assert.Equal(new OverlayLocationSettings("DISPLAY2", 10, 20), previews[2].Location);
    }

    [Fact]
    public void Visible_token_history_tracks_appearance_changes_and_reset()
    {
        var viewModel = CreateViewModel();

        Assert.Equal("Visible token history: 21 min 0 sec", viewModel.VisibleTokenHistoryText);

        viewModel.OverlayWidth = 400;
        viewModel.GraphBarWidth = 5;
        viewModel.GraphBarGap = 2;

        Assert.Equal("Visible token history: 12 min 0 sec", viewModel.VisibleTokenHistoryText);

        viewModel.ResetAppearanceToDefaults();

        Assert.Equal("Visible token history: 21 min 0 sec", viewModel.VisibleTokenHistoryText);
    }

    [Fact]
    public void General_options_are_not_applied_by_visual_preview_before_confirm()
    {
        var previews = new List<AppSettings>();
        var viewModel = CreateViewModel(previews: previews);

        viewModel.StartWithWindows = false;
        viewModel.ShowOnlyWhenChatGptRunning = true;
        viewModel.HpBarColor = ColorValue.Parse("#010203");

        var preview = Assert.Single(previews);
        Assert.True(preview.StartWithWindows);
        Assert.False(preview.ShowOnlyWhenChatGptRunning);
        Assert.Equal("#010203", preview.Colors.HpBar.ToHex());
        Assert.False(viewModel.Working.StartWithWindows);
        Assert.True(viewModel.Working.ShowOnlyWhenChatGptRunning);
    }

    [Fact]
    public void Unsafe_portable_startup_can_be_disabled_but_not_reenabled()
    {
        var viewModel = CreateViewModel(canStartWithWindows: false);

        Assert.True(viewModel.StartWithWindows);
        Assert.True(viewModel.CanStartWithWindows);

        viewModel.StartWithWindows = false;

        Assert.False(viewModel.StartWithWindows);
        Assert.False(viewModel.CanStartWithWindows);

        viewModel.StartWithWindows = true;

        Assert.False(viewModel.StartWithWindows);
    }

    [Fact]
    public void Unsafe_portable_startup_is_disabled_when_not_already_registered()
    {
        var baseline = AppSettings.Default with { StartWithWindows = false };
        var viewModel = CreateViewModel(baseline, canStartWithWindows: false);

        Assert.False(viewModel.CanStartWithWindows);
    }

    [Fact]
    public void Overlay_position_group_toggles_outline_and_drag_mode()
    {
        var modes = new List<bool>();
        var viewModel = CreateViewModel(positionModes: modes);

        viewModel.SelectedGroup = viewModel.Groups.Single(group => group.Kind == SettingsGroupKind.OverlayPosition);
        viewModel.SelectedGroup = viewModel.Groups.Single(group => group.Kind == SettingsGroupKind.Color);

        Assert.Equal([true, false], modes);
    }

    [Fact]
    public void Confirm_validates_commits_and_closes_only_after_success()
    {
        AppSettings? committed = null;
        SettingsCloseRequest? closeRequest = null;
        var viewModel = CreateViewModel(commit: settings =>
        {
            committed = settings;
            return settings;
        });
        viewModel.CloseRequested += request => closeRequest = request;
        viewModel.StartWithWindows = false;
        viewModel.OverlayWidth = 420;

        viewModel.Confirm();

        Assert.NotNull(committed);
        Assert.False(committed.StartWithWindows);
        Assert.Equal(420, committed.Appearance.OverlayWidth);
        Assert.Equal(SettingsCloseReason.Confirmed, closeRequest?.Reason);
        Assert.True(viewModel.IsClosed);
    }

    [Fact]
    public void Commit_failure_keeps_window_open()
    {
        var viewModel = CreateViewModel(
            commit: _ => throw new IOException("disk full"));
        var closeCount = 0;
        viewModel.CloseRequested += _ => closeCount++;

        Assert.Throws<IOException>(() => viewModel.Confirm());
        Assert.False(viewModel.IsClosed);
        Assert.Equal(0, closeCount);
    }

    [Theory]
    [InlineData(SettingsCancelTrigger.CancelButton)]
    [InlineData(SettingsCancelTrigger.WindowClose)]
    [InlineData(SettingsCancelTrigger.EscapeKey)]
    public void Every_cancel_path_restores_baseline_preview(SettingsCancelTrigger trigger)
    {
        var previews = new List<AppSettings>();
        var baseline = AppSettings.Default with
        {
            Location = new OverlayLocationSettings("DISPLAY1", 4, 8),
        };
        var viewModel = CreateViewModel(baseline, previews: previews);
        SettingsCloseRequest? closeRequest = null;
        viewModel.CloseRequested += request => closeRequest = request;
        viewModel.OverlayWidth = 420;
        viewModel.PreviewLocation(new OverlayLocationSettings("DISPLAY2", 10, 20));

        viewModel.Cancel(trigger);

        Assert.Equal(baseline, previews[^1]);
        Assert.Equal(baseline, viewModel.Working);
        Assert.Equal(SettingsCloseReason.Cancelled, closeRequest?.Reason);
        Assert.Equal(trigger, closeRequest?.CancelTrigger);
    }

    [Fact]
    public void Repeated_open_activates_existing_window_without_creating_another_view_model()
    {
        var createCount = 0;
        var showCount = 0;
        var activateCount = 0;
        var controller = new SettingsWindowController(
            () =>
            {
                createCount++;
                return CreateViewModel();
            },
            _ => showCount++,
            _ => activateCount++);

        var first = controller.Open();
        var second = controller.Open();

        Assert.Same(first, second);
        Assert.Equal(1, createCount);
        Assert.Equal(1, showCount);
        Assert.Equal(1, activateCount);
    }

    private static SettingsWindowViewModel CreateViewModel(
        AppSettings? baseline = null,
        List<AppSettings>? previews = null,
        List<bool>? positionModes = null,
        Func<AppSettings, AppSettings>? commit = null,
        bool canStartWithWindows = true) =>
        new(
            baseline ?? AppSettings.Default,
            settings => previews?.Add(settings),
            enabled => positionModes?.Add(enabled),
            commit ?? (settings => settings),
            canStartWithWindows);
}
