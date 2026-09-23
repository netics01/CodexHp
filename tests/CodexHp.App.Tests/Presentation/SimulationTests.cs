using CodexHp.App.Development;
using CodexHp.Core.Domain;
using Xunit;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace CodexHp.App.Tests.Presentation;

public sealed class SimulationTests
{
    [Fact]
    public void Failed_update_preset_preserves_only_previously_confirmed_sample_update()
    {
        var session = new SimulationSession(1_000_000);
        session.Select("update-failed");
        Assert.Null(session.AvailableUpdate);
        session.Select("update-available");
        var update = session.AvailableUpdate;
        Assert.NotNull(update);
        session.Select("update-failed");
        Assert.Equal(update, session.AvailableUpdate);
        session.Select("update-current");
        Assert.Null(session.AvailableUpdate);
    }

    [Fact]
    public void Developer_controls_switch_presets_hide_and_close_without_a_live_app() => StaTest.Run(() =>
    {
        var exitRequests = 0;
        using var host = new SimulationHost("normal", () => exitRequests++);
        host.Start();
        Assert.Contains("Developer tool", host.ControlWindow.Title);
        var elements = Descendants(host.ControlWindow).ToArray();
        var preset = elements.OfType<ComboBox>().Single(combo => combo.ItemsSource == SimulationSession.Presets);
        preset.SelectedItem = SimulationSession.Presets.Single(p => p.Id == "banked-zero");
        Assert.Contains(elements.OfType<TextBlock>(), text => text.Text.Contains("Confirmed zero"));
        var hide = elements.OfType<Button>().Single(button => Equals(button.Content, "Hide controls for capture"));
        hide.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.False(host.ControlWindow.IsVisible);
        Assert.Equal(0, exitRequests);
        host.ControlWindow.Close();
        Assert.Equal(1, exitRequests);
    });

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }

    [Fact]
    public void Frozen_clock_pause_resume_advance_and_restart_are_deterministic()
    {
        long elapsed = 0;
        var clock = new SimulationClock(1_000_000, () => elapsed);
        elapsed += 10_000;
        Assert.Equal(1_000_000, clock.Now);
        clock.SetFrozen(false);
        elapsed += 5_000;
        Assert.Equal(1_005_000, clock.Now);
        clock.SetFrozen(true);
        elapsed += 20_000;
        Assert.Equal(1_005_000, clock.Now);
        clock.Advance(TimeSpan.FromMinutes(1));
        Assert.Equal(1_065_000, clock.Now);
        clock.Reset();
        Assert.Equal(1_000_000, clock.Now);
        Assert.True(clock.IsFrozen);
    }

    [Theory]
    [InlineData("normal")]
    [InlineData("low-usage")]
    [InlineData("service-issue")]
    [InlineData("long-issue")]
    [InlineData("banked")]
    [InlineData("banked-zero")]
    [InlineData("banked-soon")]
    [InlineData("banked-missing")]
    [InlineData("banked-unavailable")]
    [InlineData("loading")]
    [InlineData("failed")]
    [InlineData("stale")]
    [InlineData("sign-in")]
    [InlineData("reconnect")]
    [InlineData("status-unknown")]
    [InlineData("update-available")]
    [InlineData("update-current")]
    [InlineData("update-failed")]
    public void Every_named_preset_produces_visible_content_without_external_inputs(string id)
    {
        Assert.Equal(id, SimulationLaunchOptions.Parse(["--simulate", id]));
        var session = new SimulationSession(1_000_000);
        session.Select(id);
        var state = session.CreateState(37);
        Assert.True(state.IsVisible);
        Assert.NotNull(state.Tooltip);
        Assert.Equal(37, state.TokenBuckets.Count);
        Assert.False(session.Settings.StartWithWindows);
        Assert.False(session.Settings.ShowOnlyWhenChatGptRunning);
        Assert.Equal(state.Tooltip, session.CreateState(37).Tooltip);
        Assert.Equal(state.TokenBuckets, session.CreateState(37).TokenBuckets);
    }

    [Fact]
    public void Invalid_launch_options_are_not_silently_treated_as_live_mode()
    {
        Assert.Equal("normal", SimulationLaunchOptions.Parse(["--simulate"]));
        Assert.Throws<ArgumentException>(() => SimulationLaunchOptions.Parse([]));
        Assert.Throws<ArgumentException>(() => SimulationLaunchOptions.Parse(["--simlate"]));
        Assert.Throws<ArgumentException>(() => SimulationLaunchOptions.Parse(["--simulate", "unknown"]));
        Assert.Throws<ArgumentException>(() => SimulationLaunchOptions.Parse(["--simulate", "normal", "extra"]));
    }

    [Fact]
    public void Advancing_time_changes_expiry_reset_and_graph_then_restart_restores_them()
    {
        var session = new SimulationSession(1_000_000);
        session.Select("banked-soon");
        var initial = session.CreateState(37);
        Assert.Equal("30m", initial.BankedResets!.ExpiryText);
        session.Clock.Advance(TimeSpan.FromHours(1));
        var advanced = session.CreateState(37);
        Assert.Equal("0m", advanced.BankedResets!.ExpiryText);
        Assert.Contains("Expiry passed", advanced.Tooltip);
        Assert.NotEqual(initial.HpBar.RefreshFraction, advanced.HpBar.RefreshFraction);
        session.Select("banked-soon");
        Assert.Equal(initial.Tooltip, session.CreateState(37).Tooltip);
        Assert.Equal(initial.TokenBuckets, session.CreateState(37).TokenBuckets);
    }

    [Fact]
    public void Missing_inventory_zero_credits_and_stale_data_remain_different()
    {
        var session = new SimulationSession(1_000_000);
        session.Select("banked-zero");
        Assert.Equal("0", session.CreateState(1).BankedResets!.CountText);
        session.Select("banked-unavailable");
        Assert.Equal("—", session.CreateState(1).BankedResets!.CountText);
        session.Select("banked-missing");
        Assert.Equal("3", session.CreateState(1).BankedResets!.CountText);
        Assert.Equal("—", session.CreateState(1).BankedResets!.ExpiryText);
        session.Select("stale");
        Assert.True(session.CreateState(1).HpBar.IsStale);
        Assert.True(session.CreateState(1).BankedResets!.IsStale);
        session.Select("normal");
        Assert.False(session.CreateState(1).HpBar.IsStale);
    }
}
