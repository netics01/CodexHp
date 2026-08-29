using System.Windows;
using System.Windows.Controls;
using CodexHp.App.Presentation.Settings;
using CodexHp.Core.Settings;
using Xunit;

namespace CodexHp.App.Tests.Presentation;

public sealed class Win32ColorPickerTests
{
    [Theory]
    [InlineData(0x12, 0x34, 0x56, 0x00563412u)]
    [InlineData(0xFF, 0x00, 0x80, 0x008000FFu)]
    public void Color_value_round_trips_through_native_colorref(
        byte red,
        byte green,
        byte blue,
        uint expectedColorRef)
    {
        var color = new ColorValue(red, green, blue);

        var colorRef = Win32ColorPicker.ToColorRef(color);

        Assert.Equal(expectedColorRef, colorRef);
        Assert.Equal(color, Win32ColorPicker.FromColorRef(colorRef));
    }

    [Fact]
    public void Settings_window_applies_the_color_returned_by_the_injected_picker() =>
        StaTest.Run(() =>
        {
            var selected = new ColorValue(17, 34, 51);
            var picker = new FakeColorPicker(selected);
            var viewModel = CreateViewModel();
            var original = viewModel.ManaBarColor;
            var window = new SettingsWindow(viewModel, picker);
            try
            {
                window.Show();
                FindPickButtons(window).First().RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

                Assert.Equal(original, picker.CurrentColor);
                Assert.NotEqual(0, picker.OwnerWindow);
                Assert.Equal(selected, viewModel.ManaBarColor);
            }
            finally
            {
                viewModel.Cancel(SettingsCancelTrigger.CancelButton);
            }
        });

    [Fact]
    public void Settings_window_preserves_the_color_when_the_picker_is_cancelled() =>
        StaTest.Run(() =>
        {
            var picker = new FakeColorPicker(null);
            var viewModel = CreateViewModel();
            var original = viewModel.ManaBarColor;
            var window = new SettingsWindow(viewModel, picker);
            try
            {
                window.Show();
                FindPickButtons(window).First().RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

                Assert.Equal(original, viewModel.ManaBarColor);
            }
            finally
            {
                viewModel.Cancel(SettingsCancelTrigger.CancelButton);
            }
        });

    private static SettingsWindowViewModel CreateViewModel()
    {
        var viewModel = new SettingsWindowViewModel(
            AppSettings.Default,
            _ => { },
            _ => { },
            settings => settings);
        viewModel.SelectedGroup = viewModel.Groups.Single(group => group.Kind == SettingsGroupKind.Color);
        return viewModel;
    }

    private static IEnumerable<Button> FindPickButtons(DependencyObject root)
    {
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        {
            if (child is Button { Content: "Pick" } button)
            {
                yield return button;
            }

            foreach (var descendant in FindPickButtons(child))
            {
                yield return descendant;
            }
        }
    }

    private sealed class FakeColorPicker(ColorValue? result) : IColorPicker
    {
        public nint OwnerWindow { get; private set; }

        public ColorValue? CurrentColor { get; private set; }

        public ColorValue? PickColor(nint ownerWindow, ColorValue current)
        {
            this.OwnerWindow = ownerWindow;
            this.CurrentColor = current;
            return result;
        }
    }
}
