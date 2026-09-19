using CodexHp.App.Infrastructure;
using CodexHp.App.Presentation;
using Xunit;

namespace CodexHp.App.Tests.Presentation;

public sealed class TrayIconThemeTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Startup_selects_the_matching_embedded_icon(bool light) => StaTest.Run(() =>
    {
        var expected = light ? TrayIconTheme.Light : TrayIconTheme.Dark;
        using var view = new WindowsTrayIconView(() => expected);
        Assert.Equal(expected, view.CurrentTheme);
        using var icon = System.Drawing.Icon.FromHandle(view.CurrentIconHandle);
        using var bitmap = icon.ToBitmap();
        var colored = new List<System.Drawing.Color>();
        for (var y = 0; y < bitmap.Height * 0.7; y++)
        for (var x = 0; x < bitmap.Width; x++)
        {
            var pixel = bitmap.GetPixel(x, y);
            if (pixel.A > 200) colored.Add(pixel);
        }
        Assert.NotEmpty(colored);
        Assert.All(colored, pixel => Assert.True(light ? pixel.R < 10 : pixel.R > 245));
    });

    [Theory]
    [InlineData(0x001Au)]
    [InlineData(0x031Au)]
    public void Native_theme_notification_switches_the_registered_icon_without_recreating_the_tray(uint message) =>
        StaTest.Run(() =>
    {
        TrayIconTheme? selected = TrayIconTheme.Dark;
        using var view = new WindowsTrayIconView(() => selected);
        view.Visible = true;
        view.ToolTipText = "CodexHp — theme test";
        var hwnd = view.MessageWindowHandle;
        var darkHandle = view.CurrentIconHandle;
        selected = TrayIconTheme.Light;
        _ = NativeMethods.SendMessageW(hwnd, message, nint.Zero, nint.Zero);
        Assert.Equal(TrayIconTheme.Light, view.CurrentTheme);
        var lightHandle = view.CurrentIconHandle;
        Assert.NotEqual(darkHandle, lightHandle);
        Assert.True(view.Visible);
        Assert.Equal(hwnd, view.MessageWindowHandle);
        Assert.Equal("CodexHp — theme test", view.ToolTipText);
        Assert.Equal(TrayIconController.DefaultMenuItems, view.MenuItems);

        // Duplicate notifications reuse the same icon; unavailable reads retain it.
        _ = NativeMethods.SendMessageW(hwnd, message, nint.Zero, nint.Zero);
        Assert.Equal(lightHandle, view.CurrentIconHandle);
        selected = null;
        _ = NativeMethods.SendMessageW(hwnd, message, nint.Zero, nint.Zero);
        Assert.Equal(lightHandle, view.CurrentIconHandle);
        selected = TrayIconTheme.Dark;
        _ = NativeMethods.SendMessageW(hwnd, message, nint.Zero, nint.Zero);
        Assert.Equal(darkHandle, view.CurrentIconHandle);
    });

    [Fact]
    public void Showing_a_hidden_icon_and_explorer_notification_recheck_the_latest_theme() => StaTest.Run(() =>
    {
        TrayIconTheme? selected = null;
        using var view = new WindowsTrayIconView(() => selected);
        Assert.Equal(TrayIconTheme.Dark, view.CurrentTheme);
        selected = TrayIconTheme.Light;
        var recreated = NativeMethods.RegisterWindowMessage("TaskbarCreated");
        _ = NativeMethods.SendMessageW(view.MessageWindowHandle, recreated, nint.Zero, nint.Zero);
        Assert.Equal(TrayIconTheme.Light, view.CurrentTheme);
        selected = TrayIconTheme.Dark;
        view.Visible = true;
        Assert.Equal(TrayIconTheme.Dark, view.CurrentTheme);
        view.Visible = false;
        selected = TrayIconTheme.Light;
        view.Visible = true;
        Assert.Equal(TrayIconTheme.Light, view.CurrentTheme);
        view.Dispose();
        Assert.Throws<ObjectDisposedException>(() => view.RefreshTheme());
    });
}
