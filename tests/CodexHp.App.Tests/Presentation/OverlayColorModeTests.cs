using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CodexHp.App.Presentation;
using CodexHp.App.Presentation.Settings;
using CodexHp.Core.Domain;
using CodexHp.Core.Settings;
using Xunit;

namespace CodexHp.App.Tests.Presentation;

public sealed class OverlayColorModeTests
{
    [Theory]
    [InlineData(OverlayColorMode.Light, false, true)]
    [InlineData(OverlayColorMode.Light, true, true)]
    [InlineData(OverlayColorMode.Dark, false, false)]
    [InlineData(OverlayColorMode.Dark, true, false)]
    [InlineData(OverlayColorMode.System, false, false)]
    [InlineData(OverlayColorMode.System, true, true)]
    public void Mode_resolves_palette_without_mutating_settings(OverlayColorMode mode, bool systemLight, bool expectedLight)
    {
        var settings = AppSettings.Default with { ColorMode = mode };
        var presentation = OverlayPresentationSettings.FromUnscaled(settings, systemLight);
        Assert.Equal(expectedLight, presentation.IsLight);
        Assert.Equal(expectedLight ? settings.LightColors : settings.Colors, presentation.Colors);
        Assert.Equal(ColorSettings.Default, settings.Colors);
    }

    [Fact]
    public void Separate_profiles_preview_reset_cancel_and_confirm()
    {
        var previews = new List<AppSettings>();
        var systemLight = false;
        AppSettings? committed = null;
        var baseline = AppSettings.Default with { Colors = ColorSettings.Default with { ManaBar = ColorValue.Parse("#ABCDEF") } };
        SettingsWindowViewModel Create() => new(baseline, previews.Add, null,
            value => committed = value, systemUsesLightColors: () => systemLight);
        var vm = Create();
        vm.ColorMode = OverlayColorMode.Light;
        Assert.Equal(ColorSettings.LightDefault.ManaBar, vm.ManaBarColor);
        vm.ManaBarColor = ColorValue.Parse("#445566");
        vm.ColorMode = OverlayColorMode.Dark;
        Assert.Equal(baseline.Colors.ManaBar, vm.ManaBarColor);
        vm.ResetColorsToDefaults();
        Assert.Equal(ColorSettings.Default, vm.Working.Colors);
        Assert.Equal(ColorValue.Parse("#445566"), vm.Working.LightColors.ManaBar);
        vm.ColorMode = OverlayColorMode.System;
        systemLight = true;
        vm.RefreshSystemColorMode();
        Assert.Equal(ColorValue.Parse("#445566"), vm.ManaBarColor);
        vm.ResetColorsToDefaults();
        Assert.Equal(ColorSettings.LightDefault, vm.Working.LightColors);
        vm.Cancel();
        Assert.Equal(baseline, previews[^1]);
        Assert.Null(committed);
        var confirmed = Create();
        confirmed.ColorMode = OverlayColorMode.Light;
        confirmed.Confirm();
        Assert.Equal(OverlayColorMode.Light, committed!.ColorMode);
        Assert.Equal(baseline.Colors, committed.Colors);
    }

    [Fact]
    public void Combo_is_first_and_drives_live_preview_and_cancel() => StaTest.Run(() =>
    {
        var previews = new List<AppSettings>();
        var vm = new SettingsWindowViewModel(AppSettings.Default, previews.Add, null, value => value);
        vm.SelectedGroup = vm.Groups.Single(group => group.Kind == SettingsGroupKind.Color);
        var window = new SettingsWindow(vm);
        try
        {
            window.Show();
            window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            var combo = Assert.IsType<ComboBox>(window.FindName("ColorModeComboBox"));
            Assert.Equal(new[] { OverlayColorMode.Light, OverlayColorMode.Dark, OverlayColorMode.System }, combo.Items.Cast<OverlayColorMode>());
            Assert.Equal(OverlayColorMode.System, combo.SelectedItem);
            var swatch = Assert.IsType<Button>(window.FindName("ManaColorSwatch"));
            Assert.True(combo.TranslatePoint(new Point(), window).Y < swatch.TranslatePoint(new Point(), window).Y);
            combo.SelectedItem = OverlayColorMode.Light;
            window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            Assert.Equal(OverlayColorMode.Light, previews[^1].ColorMode);
            Assert.Equal(ColorSettings.LightDefault.ManaBar, vm.ManaBarColor);
            vm.Cancel();
            Assert.Equal(AppSettings.Default, previews[^1]);
        }
        finally
        {
            if (!vm.IsClosed) vm.Cancel();
        }
    });

    [Theory]
    [InlineData(0, 1, false)]
    [InlineData(20, 1.25, false)]
    [InlineData(50, 1.5, false)]
    [InlineData(75, 2, false)]
    [InlineData(100, 3, false)]
    [InlineData(50, 2, true)]
    public void Light_text_clips_preserve_geometry_and_actual_pixels(int percent, double scale, bool stale)
    {
        var appearance = new EffectiveAppearanceSettings(
            (int)(130 * scale), (int)(30 * scale), (int)(48 * scale), Math.Max(1, (int)scale), 0, (int)(2 * scale), scale, scale);
        var state = new UsageOverlayState(true, new(percent, .6, stale), new(percent, .43, stale), [0, 2500, 100000], null, null);
        var dark = UsageOverlayRenderer.CreateLayout(state, new OverlayPresentationSettings(ColorSettings.Default, appearance), false);
        var light = UsageOverlayRenderer.CreateLayout(state, new OverlayPresentationSettings(ColorSettings.LightDefault, appearance, true), false);
        Assert.Equal(dark.Commands.Where(c => c.Kind != OverlayDrawKind.Text).Select(c => c.Bounds),
            light.Commands.Where(c => c.Kind != OverlayDrawKind.Text).Select(c => c.Bounds));
        Assert.Equal(ColorValue.Parse("#F3F3F3"), light.Commands[0].Color);
        var raster = GdiBitmapSourceRenderer.Render(light);
        var backgroundOnly = GdiBitmapSourceRenderer.Render(light with { Commands = light.Commands.Where(c => c.Kind != OverlayDrawKind.Text).ToArray() });
        var pixels = Pixels(raster);
        var withoutText = Pixels(backgroundOnly);
        var changed = 0;
        for (var y = 0; y < light.Height; y++)
        for (var x = 0; x < light.Width; x++)
        {
            var offset = (y * light.Width + x) * 4;
            if (pixels.AsSpan(offset, 3).SequenceEqual(withoutText.AsSpan(offset, 3))) continue;
            changed++;
            Assert.Contains(light.Commands, command => command.Kind == OverlayDrawKind.Text
                && command.ClipBounds is { } clip && x >= clip.Left && x < clip.Right && y >= clip.Top && y < clip.Bottom);
        }
        Assert.True(changed > 0);
        var baseline = light.Commands.Single(c => c.Role == OverlayElementRole.GraphBaseline);
        var baselineOffset = (baseline.Bounds.Top * light.Width + baseline.Bounds.Left) * 4;
        Assert.Equal(new byte[] { 0x72, 0x61, 0x53 }, pixels[baselineOffset..(baselineOffset + 3)]);
    }

    [Fact]
    public void Light_stale_fill_and_custom_pale_fill_use_readable_text()
    {
        var settings = AppSettings.Default with { ColorMode = OverlayColorMode.Light };
        var state = new UsageOverlayState(true, new(75, .6, false), new(20, .43, false), [], null, null)
        { ServiceHealth = ServiceHealthState.Issue };
        var layout = UsageOverlayRenderer.CreateLayout(state, settings, false);
        Assert.Equal(settings.LightColors.ServiceIssue, layout.Commands.Single(c => c.Role == OverlayElementRole.StatusStripe).Color);
        var text = layout.Commands.Where(c => c.Role == OverlayElementRole.ManaText).ToArray();
        Assert.Equal(ColorValue.Parse("#FFFFFF"), text[0].Color);
        Assert.Equal(ColorValue.Parse("#243042"), text[1].Color);
        var pale = settings with { LightColors = settings.LightColors with { ManaBar = ColorValue.Parse("#FFFFFF") } };
        var paleLayout = UsageOverlayRenderer.CreateLayout(state, pale, false);
        Assert.All(paleLayout.Commands.Where(c => c.Role == OverlayElementRole.ManaText), c => Assert.Equal(ColorValue.Parse("#243042"), c.Color));
        var message = UsageOverlayRenderer.CreateLayout(state with { ContentMessage = "Usage unavailable" }, settings, false);
        Assert.Equal(ColorValue.Parse("#243042"), message.Commands.Single(c => c.Role == OverlayElementRole.ContentMessage).Color);
    }

    private static byte[] Pixels(BitmapSource bitmap)
    {
        var bytes = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4];
        bitmap.CopyPixels(bytes, bitmap.PixelWidth * 4, 0);
        return bytes;
    }
}
