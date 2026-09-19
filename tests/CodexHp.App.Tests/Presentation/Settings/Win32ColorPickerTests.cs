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

    [Theory]
    [InlineData(OverlayColorMode.Light)]
    [InlineData(OverlayColorMode.Dark)]
    public void Each_color_chip_opens_picker_and_updates_only_the_selected_profile(OverlayColorMode mode) =>
        StaTest.Run(() =>
        {
            var selected = new ColorValue(17, 34, 51);
            var picker = new FakeColorPicker(selected);
            var viewModel = CreateViewModel();
            viewModel.ColorMode = mode;
            var window = new SettingsWindow(viewModel, picker);
            try
            {
                window.Show();
                var chips = new (string Name, Func<ColorValue> Get)[]
                {
                    ("ManaColorSwatch", () => viewModel.ManaBarColor),
                    ("HpColorSwatch", () => viewModel.HpBarColor),
                    ("RefreshColorSwatch", () => viewModel.RefreshGaugeColor),
                    ("IssueColorSwatch", () => viewModel.ServiceIssueColor),
                    ("UnknownColorSwatch", () => viewModel.ServiceUnknownColor),
                    ("TokenLowColorSwatch", () => viewModel.TokenLowColor),
                    ("TokenHighColorSwatch", () => viewModel.TokenHighColor),
                };
                foreach (var chip in chips)
                {
                    var before = chips.Select(item => item.Get()).ToArray();
                    var original = chip.Get();
                    var button = Assert.IsType<Button>(window.FindName(chip.Name));
                    button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    Assert.Equal(original, picker.CurrentColor);
                    Assert.NotEqual(0, picker.OwnerWindow);
                    Assert.Equal(selected, chip.Get());
                    for (var index = 0; index < chips.Length; index++)
                    {
                        if (chips[index].Name != chip.Name) Assert.Equal(before[index], chips[index].Get());
                    }
                }
                Assert.Equal(mode == OverlayColorMode.Light ? ColorSettings.Default : ColorSettings.LightDefault,
                    mode == OverlayColorMode.Light ? viewModel.Working.Colors : viewModel.Working.LightColors);
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
                Assert.IsType<Button>(window.FindName("ManaColorSwatch")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

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
