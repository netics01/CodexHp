using CodexHp.App.Presentation;
using System.Reflection;
using System.Runtime.InteropServices;
using Xunit;

namespace CodexHp.App.Tests.Presentation;

public sealed class TrayIconControllerTests
{
    [Theory]
    [InlineData(0x0202u, TrayMouseButton.Left)]
    [InlineData(0x0205u, TrayMouseButton.Right)]
    [InlineData(0x0200u, TrayMouseButton.Other)]
    public void Native_callback_messages_route_to_existing_mouse_buttons(
        uint nativeMessage,
        TrayMouseButton expected)
    {
        Assert.Equal(expected, TrayIconMessageRouter.RouteMouseButton(nativeMessage));
    }

    [Theory]
    [InlineData(1u, TrayMenuCommand.Options)]
    [InlineData(2u, TrayMenuCommand.Exit)]
    public void Native_menu_command_ids_route_in_display_order(
        uint nativeCommand,
        TrayMenuCommand expected)
    {
        Assert.Equal(expected, TrayIconMessageRouter.RouteMenuCommand(nativeCommand));
    }

    [Fact]
    public void Unknown_native_menu_command_is_ignored()
    {
        Assert.Null(TrayIconMessageRouter.RouteMenuCommand(0));
    }

    [Fact]
    public void Native_popup_menu_signature_matches_TrackPopupMenuEx()
    {
        var nativeMethods = typeof(WindowsTrayIconView).GetNestedType(
            "NativeMethods",
            BindingFlags.NonPublic);
        var method = nativeMethods?.GetMethod(
            "TrackPopupMenu",
            BindingFlags.Public | BindingFlags.Static);
        var import = method?.GetCustomAttribute<DllImportAttribute>();

        Assert.NotNull(method);
        Assert.Equal("TrackPopupMenuEx", import?.EntryPoint);
        Assert.Equal(6, method.GetParameters().Length);
    }

    [Fact]
    public void Left_click_opens_options_and_right_click_is_left_to_context_menu()
    {
        var view = new FakeTrayIconView();
        var options = 0;
        var exits = 0;
        using var controller = new TrayIconController(view, () => options++, () => exits++);

        view.RaiseMouseClick(TrayMouseButton.Left);
        view.RaiseMouseClick(TrayMouseButton.Right);

        Assert.Equal(1, options);
        Assert.Equal(0, exits);
    }

    [Fact]
    public void Context_menu_contains_options_then_exit_and_routes_each_action()
    {
        var view = new FakeTrayIconView();
        var options = 0;
        var exits = 0;
        using var controller = new TrayIconController(view, () => options++, () => exits++);

        Assert.Equal(
            [new TrayMenuItem(TrayMenuCommand.Options, "Options"), new TrayMenuItem(TrayMenuCommand.Exit, "Exit")],
            view.MenuItems);
        view.RaiseMenuCommand(TrayMenuCommand.Options);
        view.RaiseMenuCommand(TrayMenuCommand.Exit);

        Assert.Equal(1, options);
        Assert.Equal(1, exits);
    }

    [Fact]
    public void Controller_shows_icon_then_hides_and_disposes_it_on_shutdown()
    {
        var view = new FakeTrayIconView();
        var controller = new TrayIconController(view, () => { }, () => { });

        Assert.True(view.Visible);
        Assert.Equal(TrayIconAsset.CodexHpGauge, view.IconAsset);

        controller.Dispose();

        Assert.False(view.Visible);
        Assert.True(view.IsDisposed);
    }

    [Fact]
    public void Windows_view_uses_the_fixed_CodexHp_gauge_icon() =>
        StaTest.Run(() =>
        {
            using var view = new WindowsTrayIconView();

            Assert.Equal(TrayIconAsset.CodexHpGauge, view.IconAsset);
            Assert.Equal("CodexHp", view.ToolTipText);
        });

    [Fact]
    public void Windows_view_registers_and_removes_the_native_tray_icon() =>
        StaTest.Run(() =>
        {
            using var view = new WindowsTrayIconView();

            view.Visible = true;
            Assert.True(view.Visible);
            view.Visible = false;
            Assert.False(view.Visible);
        });

    [Fact]
    public void Product_icon_resource_is_embedded_and_loadable()
    {
        using var stream = typeof(WindowsTrayIconView).Assembly.GetManifestResourceStream(
            "CodexHp.App.Assets.CodexHp.ico");

        Assert.NotNull(stream);
        using var icon = new System.Drawing.Icon(stream);
        Assert.True(icon.Width >= 16);
        Assert.True(icon.Height >= 16);
    }

    [Fact]
    public void Product_icon_mark_matches_the_official_tray_icons_visual_scale()
    {
        using var stream = typeof(WindowsTrayIconView).Assembly.GetManifestResourceStream(
            "CodexHp.App.Assets.CodexHp.ico");
        Assert.NotNull(stream);
        using var icon = new System.Drawing.Icon(stream, new System.Drawing.Size(32, 32));
        using var bitmap = icon.ToBitmap();

        var brightPixels = new List<System.Drawing.Point>();
        for (var y = 0; y < 24; y++)
        {
            for (var x = 0; x < bitmap.Width; x++)
            {
                var pixel = bitmap.GetPixel(x, y);
                if (pixel.A > 200 && pixel.R > 225 && pixel.G > 225 && pixel.B > 225)
                {
                    brightPixels.Add(new System.Drawing.Point(x, y));
                }
            }
        }

        Assert.NotEmpty(brightPixels);
        var markWidth = brightPixels.Max(point => point.X) - brightPixels.Min(point => point.X) + 1;
        Assert.True(markWidth >= 26, $"The 32px Codex mark is only {markWidth}px wide.");
    }

    private sealed class FakeTrayIconView : ITrayIconView
    {
        public event Action<TrayMouseButton>? MouseClicked;

        public event Action<TrayMenuCommand>? MenuCommandInvoked;

        public bool Visible { get; set; }

        public bool IsDisposed { get; private set; }

        public TrayIconAsset IconAsset => TrayIconAsset.CodexHpGauge;

        public string ToolTipText => "CodexHp";

        public IReadOnlyList<TrayMenuItem> MenuItems { get; } = TrayIconController.DefaultMenuItems;

        public void RaiseMouseClick(TrayMouseButton button) => this.MouseClicked?.Invoke(button);

        public void RaiseMenuCommand(TrayMenuCommand command) => this.MenuCommandInvoked?.Invoke(command);

        public void Dispose() => this.IsDisposed = true;
    }
}
