using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using CodexHp.App.Infrastructure;
using CodexHp.Core.Domain;
using CodexHp.Core.Positioning;

namespace CodexHp.App.Presentation;

internal sealed class CustomOverlayTooltip : IDisposable
{
    private readonly nint target;
    private readonly Window window;
    private readonly HwndSource source;
    private readonly DispatcherTimer timer;
    private OverlayTooltipView? view;
    private string? text;
    private bool isLight;
    private long? enteredAt;
    private long? leftAt;
    private bool dismissed;
    private bool disposed;
    private PhysicalRect? lastAnchor;
    private PhysicalRect? lastWorkArea;
    private double lastScale;
    private bool viewChanged;

    internal CustomOverlayTooltip(nint target)
    {
        if (!NativeMethods.IsWindow(target)) throw new ArgumentException("A live overlay is required.", nameof(target));
        this.target = target;
        this.window = new Window
        {
            Title = "CodexHp tooltip", Width = 320, Height = 80,
            WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize,
            AllowsTransparency = true, Background = Brushes.Transparent,
            ShowActivated = false, ShowInTaskbar = false, Topmost = true, Focusable = false,
            UseLayoutRounding = true,
        };
        this.WindowHandle = new WindowInteropHelper(this.window).EnsureHandle();
        AltTabWindowStyle.Apply(this.WindowHandle);
        var style = NativeMethods.GetWindowLongPointer(this.WindowHandle, NativeMethods.GwlExStyle);
        if (!NativeMethods.TrySetWindowLongPointer(this.WindowHandle, NativeMethods.GwlExStyle,
            style | (nint)NativeMethods.WsExNoActivate, out var error))
            throw new System.ComponentModel.Win32Exception(error);
        this.source = HwndSource.FromHwnd(this.WindowHandle)!;
        this.source.AddHook(this.WindowHook);
        this.timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(50) };
        this.timer.Tick += this.OnTick;
    }

    internal nint WindowHandle { get; }
    internal bool IsEnabled => this.text is not null;
    internal bool IsVisible => this.window.IsVisible;
#if CODEXHP_DEVELOPMENT
    internal Development.SimulationTooltipMode SimulationMode { get; set; }
#endif

    internal void Update(string? text, IReadOnlyList<OverlayTooltipSection>? sections = null, bool isLight = false)
    {
        ObjectDisposedException.ThrowIf(this.disposed, this);
        text = string.IsNullOrWhiteSpace(text) ? null : text;
        if (this.text == text && this.isLight == isLight) return;
        this.text = text;
        this.isLight = isLight;
        if (text is null)
        {
            this.timer.Stop();
            this.Hide();
            this.dismissed = false;
            return;
        }
        // Match the app's Fluent XAML theme support, including the overflow scrollbar.
#pragma warning disable WPF0001
        this.window.ThemeMode = isLight ? ThemeMode.Light : ThemeMode.Dark;
#pragma warning restore WPF0001
        this.view = new OverlayTooltipView(sections is { Count: > 0 } ? sections : [new(null, [new("", text)])], isLight);
        this.window.Content = this.view;
        this.viewChanged = true;
        System.Windows.Automation.AutomationProperties.SetName(this.view, text);
        this.timer.Start();
        if (this.IsVisible) this.ShowAtAnchor();
    }

    // Kept independent of real cursor sampling so hover timing is deterministic in tests.
    internal void ProcessHover(bool overAnchor, bool overTooltip, bool dismiss, long now)
    {
        if (!this.IsEnabled) return;
        if (dismiss)
        {
            this.dismissed = true;
            this.Hide();
            return;
        }
        if (overAnchor)
        {
            this.leftAt = null;
            if (this.dismissed) return;
            this.enteredAt ??= now;
            if (now - this.enteredAt >= 200) this.ShowAtAnchor();
        }
        else if (overTooltip && this.IsVisible)
        {
            this.leftAt = null;
            this.ShowAtAnchor();
        }
        else
        {
            this.dismissed = false;
            this.enteredAt = null;
            this.leftAt ??= now;
            // A short grace period lets the pointer cross the gap to scroll long incidents.
            if (now - this.leftAt >= 250) this.Hide();
        }
    }

    internal void ShowAtAnchor()
    {
        if (!this.IsEnabled || this.view is null || !NativeMethods.IsWindowVisible(this.target)
            || !NativeMethods.GetWindowRect(this.target, out var rect))
        {
            this.Hide();
            return;
        }
        var monitor = NativeMethods.MonitorFromWindow(this.target, NativeMethods.MonitorDefaultToNearest);
        var info = new NativeMethods.MonitorInfoEx { Size = Marshal.SizeOf<NativeMethods.MonitorInfoEx>() };
        if (!NativeMethods.GetMonitorInfo(monitor, ref info)) { this.Hide(); return; }
        var dpi = NativeMethods.GetDpiForWindow(this.target);
        var scale = (dpi == 0 ? 96 : dpi) / 96d;
        var area = new PhysicalRect(info.WorkArea.Left, info.WorkArea.Top,
            info.WorkArea.Right - info.WorkArea.Left, info.WorkArea.Bottom - info.WorkArea.Top);
        var anchor = new PhysicalRect(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top);
        if (this.IsVisible && !this.viewChanged && anchor == this.lastAnchor && area == this.lastWorkArea && scale == this.lastScale)
            return;
        var maximumWidthDip = Math.Max(1, area.Width / scale - 12);
        this.view.SetTail(TooltipTailEdge.Bottom, 40);
        var preferred = this.view.MeasurePreferredSize(maximumWidthDip);
        var placement = OverlayTooltipPlacement.Calculate(
            anchor, area,
            (int)Math.Ceiling(preferred.Width * scale), (int)Math.Ceiling(preferred.Height * scale), scale);
        if (placement is null) { this.Hide(); return; }
        this.view.SetTail(placement.TailEdge, placement.TailOffset / scale);
        this.window.Width = placement.Bounds.Width / scale;
        this.window.Height = placement.Bounds.Height / scale;
        // Move the HWND to the target monitor before showing so WPF adopts that monitor's DPI.
        this.Place(placement.Bounds);
        if (!this.IsVisible) this.window.Show();
        this.Place(placement.Bounds);
        this.view.Measure(new Size(this.window.Width, this.window.Height));
        this.window.UpdateLayout();
        this.lastAnchor = anchor;
        this.lastWorkArea = area;
        this.lastScale = scale;
        this.viewChanged = false;
    }

    private void Place(PhysicalRect bounds) => NativeMethods.SetWindowPos(this.WindowHandle, NativeMethods.HwndTopmost,
        bounds.Left, bounds.Top, bounds.Width, bounds.Height, NativeMethods.SwpNoActivate);

    private void OnTick(object? sender, EventArgs args)
    {
#if CODEXHP_DEVELOPMENT
        if (this.SimulationMode == Development.SimulationTooltipMode.Pinned) { this.ShowAtAnchor(); return; }
        if (this.SimulationMode == Development.SimulationTooltipMode.Hidden) { this.Hide(); return; }
#endif
        if (!NativeMethods.IsWindowVisible(this.target)) { this.Hide(); return; }
        if (!NativeMethods.GetCursorPos(out var cursor)) { this.Hide(); return; }
        var hit = NativeMethods.WindowFromPoint(cursor);
        var overAnchor = hit == this.target || NativeMethods.IsChild(this.target, hit);
        var overTooltip = hit == this.WindowHandle || NativeMethods.IsChild(this.WindowHandle, hit);
        var dismiss = (NativeMethods.GetAsyncKeyState(0x1B) & 0x8000) != 0
            || (NativeMethods.GetAsyncKeyState(0x01) & 0x8000) != 0 && overAnchor;
        this.ProcessHover(overAnchor, overTooltip, dismiss, Environment.TickCount64);
    }

    private nint WindowHook(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (message == 0x0021) // WM_MOUSEACTIVATE: preserve the user's foreground application.
        {
            handled = true;
            return new nint(3); // MA_NOACTIVATE
        }
        return nint.Zero;
    }

    private void Hide()
    {
        this.window.Hide();
        this.enteredAt = null;
        this.leftAt = null;
    }

    public void Dispose()
    {
        if (this.disposed) return;
        this.disposed = true;
        this.timer.Stop();
        this.timer.Tick -= this.OnTick;
        this.source.RemoveHook(this.WindowHook);
        this.window.Close();
    }
}
