using System.ComponentModel;
using System.Runtime.InteropServices;
using CodexHp.App.Infrastructure;

namespace CodexHp.App.Presentation;

internal sealed class NativeOverlayTooltip : IDisposable
{
    private const string TooltipClassName = "tooltips_class32";
    private const int InitialDelayMilliseconds = 200;
    private static readonly uint ToolInfoVersion2Size = checked((uint)Marshal.OffsetOf<NativeMethods.ToolInfo>(
        nameof(NativeMethods.ToolInfo.Reserved)).ToInt64());
    private readonly nint targetWindowHandle;
    private nint tooltipWindowHandle;
    private GCHandle textHandle;
    private bool hasPinnedText;
    private bool isEnabled;
    private bool isDisposed;

    internal NativeOverlayTooltip(nint targetWindowHandle)
    {
        if (targetWindowHandle == nint.Zero || !NativeMethods.IsWindow(targetWindowHandle))
        {
            throw new ArgumentException("A live usage overlay window is required.", nameof(targetWindowHandle));
        }

        this.targetWindowHandle = targetWindowHandle;
        this.InitializeCommonControls();
        this.tooltipWindowHandle = NativeMethods.CreateWindowEx(
            NativeMethods.WsExTopmost | NativeMethods.WsExToolWindow | NativeMethods.WsExNoActivate,
            TooltipClassName,
            null,
            NativeMethods.WsPopup | NativeMethods.TtsAlwaysTip | NativeMethods.TtsNoPrefix,
            0,
            0,
            0,
            0,
            targetWindowHandle,
            nint.Zero,
            nint.Zero,
            nint.Zero);
        if (this.tooltipWindowHandle == nint.Zero)
        {
            throw new Win32Exception(
                Marshal.GetLastWin32Error(),
                "The usage overlay status tooltip could not be created.");
        }

        this.ReplaceText(string.Empty);
        this.SendToolInfo(NativeMethods.TtmAddToolW);
        _ = NativeMethods.SendMessageW(
            this.tooltipWindowHandle,
            NativeMethods.TtmSetDelayTime,
            new nint(NativeMethods.TtDtInitial),
            new nint(InitialDelayMilliseconds));
        _ = NativeMethods.SendMessageW(
            this.tooltipWindowHandle,
            NativeMethods.TtmSetMaxTipWidth,
            nint.Zero,
            new nint(this.GetVirtualScreenWidth()));
        this.isEnabled = true;
        this.SetEnabled(false);
    }

    internal nint WindowHandle => this.tooltipWindowHandle;

    internal bool IsEnabled => this.isEnabled;

    private int GetVirtualScreenWidth() => Math.Max(
        1,
        NativeMethods.GetSystemMetrics(NativeMethods.SmCxVirtualScreen));

    internal void Update(string? text)
    {
        ObjectDisposedException.ThrowIf(this.isDisposed, this);
        var normalized = string.IsNullOrWhiteSpace(text) ? null : text.Trim();
        if (normalized is null)
        {
            this.SetEnabled(false);
            return;
        }

        this.ReplaceText(normalized);
        this.SendToolInfo(NativeMethods.TtmUpdateTipTextW);
        this.SetEnabled(true);
    }

    public void Dispose()
    {
        if (this.isDisposed)
        {
            return;
        }

        this.isDisposed = true;
        this.SetEnabled(false);
        if (this.tooltipWindowHandle != nint.Zero)
        {
            _ = NativeMethods.DestroyWindow(this.tooltipWindowHandle);
            this.tooltipWindowHandle = nint.Zero;
        }

        this.ReleasePinnedText();
    }

    private void InitializeCommonControls()
    {
        var controls = new NativeMethods.InitCommonControlsData
        {
            Size = checked((uint)Marshal.SizeOf<NativeMethods.InitCommonControlsData>()),
            Classes = NativeMethods.IccWin95Classes,
        };
        if (!NativeMethods.InitCommonControlsEx(ref controls))
        {
            throw new Win32Exception(
                Marshal.GetLastWin32Error(),
                "Windows common controls could not be initialized for the usage overlay tooltip.");
        }
    }

    private void SetEnabled(bool isEnabled)
    {
        if (this.tooltipWindowHandle == nint.Zero || this.isEnabled == isEnabled)
        {
            return;
        }

        if (!isEnabled)
        {
            _ = NativeMethods.SendMessageW(
                this.tooltipWindowHandle,
                NativeMethods.TtmPop,
                nint.Zero,
                nint.Zero);
        }

        _ = NativeMethods.SendMessageW(
            this.tooltipWindowHandle,
            NativeMethods.TtmActivate,
            isEnabled ? new nint(1) : nint.Zero,
            nint.Zero);
        this.isEnabled = isEnabled;
    }

    private void ReplaceText(string text)
    {
        var replacement = GCHandle.Alloc(text, GCHandleType.Pinned);
        this.ReleasePinnedText();
        this.textHandle = replacement;
        this.hasPinnedText = true;
    }

    private void ReleasePinnedText()
    {
        if (!this.hasPinnedText)
        {
            return;
        }

        this.textHandle.Free();
        this.hasPinnedText = false;
    }

    private void SendToolInfo(uint message)
    {
        if (this.tooltipWindowHandle == nint.Zero || !this.hasPinnedText)
        {
            return;
        }

        var toolInfo = new NativeMethods.ToolInfo
        {
            Size = ToolInfoVersion2Size,
            Flags = NativeMethods.TtfIdIsHwnd | NativeMethods.TtfSubclass,
            WindowHandle = this.targetWindowHandle,
            Id = unchecked((nuint)this.targetWindowHandle),
            TextPointer = this.textHandle.AddrOfPinnedObject(),
        };
        var toolInfoMemory = Marshal.AllocHGlobal(Marshal.SizeOf<NativeMethods.ToolInfo>());
        try
        {
            Marshal.StructureToPtr(toolInfo, toolInfoMemory, false);
            _ = NativeMethods.SendMessageW(
                this.tooltipWindowHandle,
                message,
                nint.Zero,
                toolInfoMemory);
        }
        finally
        {
            Marshal.DestroyStructure<NativeMethods.ToolInfo>(toolInfoMemory);
            Marshal.FreeHGlobal(toolInfoMemory);
        }
    }
}
