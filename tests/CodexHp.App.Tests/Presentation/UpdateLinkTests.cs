using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using CodexHp.App.Application;
using CodexHp.App.Presentation.Settings;
using CodexHp.Core.Settings;
using Xunit;

namespace CodexHp.App.Tests.Presentation;

public sealed class UpdateLinkTests
{
    [Theory]
    [InlineData(SettingsGroupKind.General)]
    [InlineData(SettingsGroupKind.OverlayPosition)]
    public void Update_link_is_dynamic_and_does_not_navigate_or_close_settings(SettingsGroupKind group) => StaTest.Run(() =>
    {
        var opened = new List<AvailableUpdate>();
        var model = new SettingsWindowViewModel(AppSettings.Default, null, null, settings => settings, openUpdate: opened.Add);
        model.SelectedGroup = model.Groups.Single(item => item.Kind == group);
        var selected = model.SelectedGroup;
        var window = new SettingsWindow(model);
        try
        {
            window.Show();
            Pump();
            var button = Assert.IsType<Button>(window.FindName("UpdateLinkButton"));
            Assert.False(button.IsVisible);
            var update = AvailableUpdate.FromTag("v0.5.0");
            model.SetAvailableUpdate(update);
            Pump();
            Assert.True(button.IsVisible);
            Assert.Equal(HorizontalAlignment.Center, button.HorizontalContentAlignment);
            var content = Assert.IsType<StackPanel>(button.Content);
            var center = content.TranslatePoint(new Point(content.ActualWidth / 2, 0), button).X;
            Assert.InRange(Math.Abs(center - button.ActualWidth / 2), 0, 1);
            Assert.Contains("0.5.0", model.UpdateLinkDescription);
            button.Focus();
            Assert.Empty(opened);
            Assert.Equal(selected, model.SelectedGroup);
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(update, Assert.Single(opened));
            Assert.Equal(selected, model.SelectedGroup);
            Assert.False(model.IsClosed);
            Assert.True(model.HasAvailableUpdate);
            model.SetAvailableUpdate(null);
            Pump();
            Assert.False(button.IsVisible);
            model.OpenUpdatePage();
            Assert.Single(opened);
        }
        finally { window.Close(); Pump(); }
    });

    private static void Pump() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
}
