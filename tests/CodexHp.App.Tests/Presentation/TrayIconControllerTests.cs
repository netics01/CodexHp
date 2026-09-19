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
    [InlineData(3u, TrayMenuCommand.Repository)]
    public void Native_menu_command_ids_route_to_the_matching_actions(
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
    public void Context_menu_contains_settings_repository_and_separated_exit_and_routes_each_action()
    {
        var view = new FakeTrayIconView();
        var options = 0;
        var exits = 0;
        var repositories = 0;
        using var controller = new TrayIconController(view, () => options++, () => exits++, () => repositories++);

        Assert.Equal(
            [new TrayMenuItem(TrayMenuCommand.Options, "Settings"),
             new TrayMenuItem(TrayMenuCommand.Repository, "Open GitHub"),
             new TrayMenuItem(TrayMenuCommand.Exit, "Exit", SeparatorBefore: true)],
            view.MenuItems);
        view.RaiseMenuCommand(TrayMenuCommand.Repository);
        Assert.Equal(1, repositories);
        Assert.Equal(0, options);
        Assert.Equal(0, exits);
        view.RaiseMenuCommand(TrayMenuCommand.Options);
        view.RaiseMenuCommand(TrayMenuCommand.Exit);

        Assert.Equal(1, options);
        Assert.Equal(1, exits);
    }

    [Fact]
    public void Repository_link_uses_the_system_browser_for_the_public_repository()
    {
        var launch = RepositoryBrowser.CreateStartInfo();
        Assert.Equal("https://github.com/netics01/codexhp", launch.FileName);
        Assert.True(launch.UseShellExecute);
        Assert.Empty(launch.Arguments);
    }

    [Fact]
    public void Native_context_menu_has_the_repository_and_a_real_separator_before_exit()
    {
        var menu = MenuProbe.CreatePopupMenu();
        Assert.NotEqual(nint.Zero, menu);
        try
        {
            Assert.True(WindowsTrayIconView.AppendContextMenu(menu));
            Assert.Equal(4, MenuProbe.GetMenuItemCount(menu));
            var expected = new[] { (0, 1u, "Settings"), (1, 3u, "Open GitHub"), (3, 2u, "Exit") };
            foreach (var (position, id, label) in expected)
            {
                var text = new System.Text.StringBuilder(100);
                Assert.True(MenuProbe.GetMenuString(menu, (uint)position, text, text.Capacity, 0x400) > 0);
                Assert.Equal(label, text.ToString());
                Assert.Equal(id, MenuProbe.GetMenuItemID(menu, position));
            }
            Assert.Equal(0x800u, MenuProbe.GetMenuState(menu, 2, 0x400) & 0x800u);
        }
        finally
        {
            _ = MenuProbe.DestroyMenu(menu);
        }
    }

    private static class MenuProbe
    {
        [DllImport("user32.dll")]
        internal static extern nint CreatePopupMenu();
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool DestroyMenu(nint menu);
        [DllImport("user32.dll")]
        internal static extern int GetMenuItemCount(nint menu);
        [DllImport("user32.dll")]
        internal static extern uint GetMenuItemID(nint menu, int position);
        [DllImport("user32.dll")]
        internal static extern uint GetMenuState(nint menu, uint item, uint flags);
        [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetMenuStringW")]
        internal static extern int GetMenuString(nint menu, uint item, System.Text.StringBuilder text, int capacity, uint flags);
    }

    [Fact]
    public void Controller_updates_the_tray_tooltip_for_overlay_messages()
    {
        var view = new FakeTrayIconView();
        using var controller = new TrayIconController(view, () => { }, () => { });

        controller.SetStatusMessage("Sign in to Codex");
        Assert.Equal("CodexHp — Sign in to Codex", view.ToolTipText);

        controller.SetStatusMessage(null);
        Assert.Equal("CodexHp", view.ToolTipText);
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

    [Theory]
    [InlineData(16, false)]
    [InlineData(20, false)]
    [InlineData(24, false)]
    [InlineData(32, false)]
    [InlineData(40, false)]
    [InlineData(48, false)]
    [InlineData(64, false)]
    [InlineData(128, false)]
    [InlineData(256, false)]
    [InlineData(16, true)]
    [InlineData(20, true)]
    [InlineData(24, true)]
    [InlineData(32, true)]
    [InlineData(40, true)]
    [InlineData(48, true)]
    [InlineData(64, true)]
    [InlineData(128, true)]
    [InlineData(256, true)]
    public void Icon_frames_have_transparent_logo_holes_without_a_matte_and_keep_the_hp_bar(int size, bool light)
    {
        using var stream = typeof(WindowsTrayIconView).Assembly.GetManifestResourceStream(
            light ? "CodexHp.App.Assets.CodexHp.Light.ico" : "CodexHp.App.Assets.CodexHp.ico");
        Assert.NotNull(stream);
        // Inspect the requested frame itself: System.Drawing.Icon may select
        // 128px for a 256px request (ICO encodes 256 as zero in the directory).
        using var reader = new System.IO.BinaryReader(stream);
        Assert.Equal(0, reader.ReadUInt16());
        Assert.Equal(1, reader.ReadUInt16());
        var count = reader.ReadUInt16();
        var frameOffset = 0;
        var frameLength = 0;
        for (var index = 0; index < count; index++)
        {
            stream.Position = 6 + index * 16;
            var encodedWidth = reader.ReadByte();
            stream.Position += 7;
            var length = reader.ReadInt32();
            var offset = reader.ReadInt32();
            if ((encodedWidth == 0 ? 256 : encodedWidth) == size)
            {
                frameOffset = offset;
                frameLength = length;
                break;
            }
        }
        Assert.True(frameLength > 0, $"Missing {size}px icon frame.");
        stream.Position = frameOffset;
        using var frame = new System.IO.MemoryStream(reader.ReadBytes(frameLength));
        using var bitmap = new System.Drawing.Bitmap(frame);
        Assert.Equal(size, bitmap.Width);
        Assert.Equal(size, bitmap.Height);
        Assert.Equal(0, bitmap.GetPixel(size / 2, (int)Math.Round(size * 0.43)).A);
        Assert.Equal(0, bitmap.GetPixel(0, 0).A);
        for (var x = 0; x < size; x++)
        {
            // Small frames can carry a faint resampling fringe in the first row,
            // but the opaque curve must never be cut off by the canvas edge.
            Assert.True(bitmap.GetPixel(x, 0).A <= (size >= 40 ? 0 : 128),
                $"The logo touches the upper edge in the {size}px frame at x={x}.");
        }

        var hasRedGauge = false;
        for (var y = 0; y < size; y++)
        for (var x = 0; x < size; x++)
        {
            var pixel = bitmap.GetPixel(x, y);
            if (y < size * 0.7 && pixel.A > 16)
            {
                Assert.True(light
                    ? pixel.R <= 10 && pixel.G <= 10 && pixel.B <= 10
                    : pixel.R >= 245 && pixel.G >= 245 && pixel.B >= 245,
                    $"Matte fringe in {size}px frame (light={light}) at ({x}, {y}): {pixel}");
            }
            hasRedGauge |= y >= size * 0.75 && pixel.A > 128 && pixel.R > pixel.G + 70;
        }
        Assert.True(hasRedGauge);
    }

    private sealed class FakeTrayIconView : ITrayIconView
    {
        public event Action<TrayMouseButton>? MouseClicked;

        public event Action<TrayMenuCommand>? MenuCommandInvoked;

        public bool Visible { get; set; }

        public bool IsDisposed { get; private set; }

        public TrayIconAsset IconAsset => TrayIconAsset.CodexHpGauge;

        public string ToolTipText { get; set; } = "CodexHp";

        public IReadOnlyList<TrayMenuItem> MenuItems { get; } = TrayIconController.DefaultMenuItems;

        public void RaiseMouseClick(TrayMouseButton button) => this.MouseClicked?.Invoke(button);

        public void RaiseMenuCommand(TrayMenuCommand command) => this.MenuCommandInvoked?.Invoke(command);

        public void Dispose() => this.IsDisposed = true;
    }
}
