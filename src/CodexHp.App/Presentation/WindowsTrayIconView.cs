using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace CodexHp.App.Presentation;

public sealed class WindowsTrayIconView : ITrayIconView
{
    private const string IconResourceName = "CodexHp.App.Assets.CodexHp.ico";
    private const string ToolTip = "CodexHp";
    private const int WindowStylePopup = unchecked((int)0x80000000);
    private const int WindowStyleExToolWindow = 0x00000080;
    private const int WindowStyleExNoActivate = 0x08000000;
    private const int TrayCallbackMessage = 0x8001;
    private const uint NotifyIconAdd = 0x00000000;
    private const uint NotifyIconDelete = 0x00000002;
    private const uint NotifyIconMessage = 0x00000001;
    private const uint NotifyIconIcon = 0x00000002;
    private const uint NotifyIconTip = 0x00000004;
    private const uint MenuString = 0x00000000;
    private const uint TrackRightButton = 0x00000002;
    private const uint TrackReturnCommand = 0x00000100;
    private const uint OptionsCommandId = 1;
    private const uint ExitCommandId = 2;
    private const uint WindowMessageNull = 0x0000;
    private readonly System.Drawing.Icon icon;
    private readonly HwndSource messageSource;
    private readonly uint taskbarCreatedMessage;
    private bool visible;
    private bool disposed;

    public WindowsTrayIconView()
    {
        this.icon = LoadIcon();
        try
        {
            var parameters = new HwndSourceParameters("CodexHp.TrayIconMessageWindow")
            {
                Width = 0,
                Height = 0,
                PositionX = -32000,
                PositionY = -32000,
                WindowStyle = WindowStylePopup,
                ExtendedWindowStyle = WindowStyleExToolWindow | WindowStyleExNoActivate,
            };
            this.messageSource = new HwndSource(parameters);
            this.messageSource.AddHook(this.OnWindowMessage);
            this.taskbarCreatedMessage = NativeMethods.RegisterWindowMessage("TaskbarCreated");
        }
        catch
        {
            this.icon.Dispose();
            throw;
        }
    }

    public event Action<TrayMouseButton>? MouseClicked;

    public event Action<TrayMenuCommand>? MenuCommandInvoked;

    public bool Visible
    {
        get => this.visible;
        set
        {
            ObjectDisposedException.ThrowIf(this.disposed, this);
            if (this.visible == value)
            {
                return;
            }

            if (value)
            {
                if (!this.AddIcon())
                {
                    throw new InvalidOperationException(
                        "CodexHp could not add its notification area icon.");
                }
            }
            else
            {
                this.DeleteIcon();
            }

            this.visible = value;
        }
    }

    public TrayIconAsset IconAsset => TrayIconAsset.CodexHpGauge;

    public string ToolTipText => ToolTip;

    public IReadOnlyList<TrayMenuItem> MenuItems => TrayIconController.DefaultMenuItems;

    public void Dispose()
    {
        if (this.disposed)
        {
            return;
        }

        if (this.visible)
        {
            this.DeleteIcon();
            this.visible = false;
        }

        this.disposed = true;
        this.messageSource.RemoveHook(this.OnWindowMessage);
        this.messageSource.Dispose();
        this.icon.Dispose();
    }

    private static System.Drawing.Icon LoadIcon()
    {
        using var iconStream = typeof(WindowsTrayIconView).Assembly.GetManifestResourceStream(
            IconResourceName)
            ?? throw new InvalidOperationException(
                $"The embedded icon resource is unavailable: {IconResourceName}");
        using var embeddedIcon = new System.Drawing.Icon(iconStream);
        return (System.Drawing.Icon)embeddedIcon.Clone();
    }

    private bool AddIcon()
    {
        var data = this.CreateNotifyIconData();
        return NativeMethods.ShellNotifyIcon(NotifyIconAdd, ref data);
    }

    private void DeleteIcon()
    {
        var data = this.CreateNotifyIconData();
        _ = NativeMethods.ShellNotifyIcon(NotifyIconDelete, ref data);
    }

    private NotifyIconData CreateNotifyIconData() => new()
    {
        Size = (uint)Marshal.SizeOf<NotifyIconData>(),
        WindowHandle = this.messageSource.Handle,
        IconId = 1,
        Flags = NotifyIconMessage | NotifyIconIcon | NotifyIconTip,
        CallbackMessage = TrayCallbackMessage,
        IconHandle = this.icon.Handle,
        Tip = ToolTip,
        Info = string.Empty,
        InfoTitle = string.Empty,
    };

    private nint OnWindowMessage(
        nint windowHandle,
        int message,
        nint wordParameter,
        nint longParameter,
        ref bool handled)
    {
        if (this.taskbarCreatedMessage != 0 && unchecked((uint)message) == this.taskbarCreatedMessage)
        {
            if (this.visible)
            {
                _ = this.AddIcon();
            }

            return 0;
        }

        if (message != TrayCallbackMessage)
        {
            return 0;
        }

        var button = TrayIconMessageRouter.RouteMouseButton(unchecked((uint)longParameter.ToInt64()));
        if (button == TrayMouseButton.Other)
        {
            return 0;
        }

        handled = true;
        this.MouseClicked?.Invoke(button);
        if (button == TrayMouseButton.Right)
        {
            this.ShowContextMenu();
        }

        return 0;
    }

    private void ShowContextMenu()
    {
        var menuHandle = NativeMethods.CreatePopupMenu();
        if (menuHandle == 0)
        {
            return;
        }

        try
        {
            foreach (var item in TrayIconController.DefaultMenuItems)
            {
                var commandId = item.Command switch
                {
                    TrayMenuCommand.Options => OptionsCommandId,
                    TrayMenuCommand.Exit => ExitCommandId,
                    _ => 0u,
                };
                var appended = commandId != 0 &&
                    NativeMethods.AppendMenu(menuHandle, MenuString, commandId, item.Text);
                if (!appended)
                {
                    return;
                }
            }

            var gotCursor = NativeMethods.GetCursorPosition(out var point);
            if (!gotCursor)
            {
                return;
            }

            _ = NativeMethods.SetForegroundWindow(this.messageSource.Handle);
            var selectedCommand = NativeMethods.TrackPopupMenu(
                menuHandle,
                TrackRightButton | TrackReturnCommand,
                point.X,
                point.Y,
                this.messageSource.Handle,
                0);
            _ = NativeMethods.PostMessage(
                this.messageSource.Handle,
                WindowMessageNull,
                0,
                0);
            if (TrayIconMessageRouter.RouteMenuCommand(selectedCommand) is { } command)
            {
                this.MenuCommandInvoked?.Invoke(command);
            }
        }
        finally
        {
            _ = NativeMethods.DestroyMenu(menuHandle);
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NotifyIconData
    {
        public uint Size;
        public nint WindowHandle;
        public uint IconId;
        public uint Flags;
        public uint CallbackMessage;
        public nint IconHandle;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string Tip;

        public uint State;
        public uint StateMask;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string Info;

        public uint TimeoutOrVersion;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string InfoTitle;

        public uint InfoFlags;
        public Guid ItemGuid;
        public nint BalloonIconHandle;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    private static class NativeMethods
    {
        [DllImport("shell32.dll", CharSet = CharSet.Unicode, EntryPoint = "Shell_NotifyIconW")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool ShellNotifyIcon(uint message, ref NotifyIconData data);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "RegisterWindowMessageW")]
        public static extern uint RegisterWindowMessage(string message);

        [DllImport("user32.dll")]
        public static extern nint CreatePopupMenu();

        [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "AppendMenuW")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool AppendMenu(
            nint menuHandle,
            uint flags,
            nuint itemIdentifier,
            string text);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool DestroyMenu(nint menuHandle);

        [DllImport("user32.dll", EntryPoint = "GetCursorPos")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetCursorPosition(out NativePoint point);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetForegroundWindow(nint windowHandle);

        [DllImport("user32.dll", EntryPoint = "TrackPopupMenuEx")]
        public static extern uint TrackPopupMenu(
            nint menuHandle,
            uint flags,
            int x,
            int y,
            nint windowHandle,
            nint parameters);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "PostMessageW")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool PostMessage(
            nint windowHandle,
            uint message,
            nint wordParameter,
            nint longParameter);
    }
}
