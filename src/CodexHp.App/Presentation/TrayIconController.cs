namespace CodexHp.App.Presentation;

public enum TrayMouseButton
{
    Other,
    Left,
    Right,
}

public enum TrayMenuCommand
{
    Options,
    Exit,
}

public enum TrayIconAsset
{
    CodexHpGauge,
}

public sealed record TrayMenuItem(TrayMenuCommand Command, string Text);

internal static class TrayIconMessageRouter
{
    private const uint LeftButtonUp = 0x0202;
    private const uint RightButtonUp = 0x0205;
    private const uint OptionsCommandId = 1;
    private const uint ExitCommandId = 2;

    public static TrayMouseButton RouteMouseButton(uint nativeMessage) => nativeMessage switch
    {
        LeftButtonUp => TrayMouseButton.Left,
        RightButtonUp => TrayMouseButton.Right,
        _ => TrayMouseButton.Other,
    };

    public static TrayMenuCommand? RouteMenuCommand(uint nativeCommand) => nativeCommand switch
    {
        OptionsCommandId => TrayMenuCommand.Options,
        ExitCommandId => TrayMenuCommand.Exit,
        _ => null,
    };
}

public interface ITrayIconView : IDisposable
{
    event Action<TrayMouseButton>? MouseClicked;

    event Action<TrayMenuCommand>? MenuCommandInvoked;

    bool Visible { get; set; }

    TrayIconAsset IconAsset { get; }

    string ToolTipText { get; }

    IReadOnlyList<TrayMenuItem> MenuItems { get; }
}

public sealed class TrayIconController : IDisposable
{
    public static IReadOnlyList<TrayMenuItem> DefaultMenuItems { get; } =
    [
        new TrayMenuItem(TrayMenuCommand.Options, "Settings"),
        new TrayMenuItem(TrayMenuCommand.Exit, "Exit"),
    ];

    private readonly ITrayIconView view;
    private readonly Action openOptions;
    private readonly Action exit;
    private bool disposed;

    public TrayIconController(Action openOptions, Action exit)
        : this(new WindowsTrayIconView(), openOptions, exit)
    {
    }

    public TrayIconController(ITrayIconView view, Action openOptions, Action exit)
    {
        this.view = view ?? throw new ArgumentNullException(nameof(view));
        this.openOptions = openOptions ?? throw new ArgumentNullException(nameof(openOptions));
        this.exit = exit ?? throw new ArgumentNullException(nameof(exit));
        this.view.MouseClicked += this.OnMouseClicked;
        this.view.MenuCommandInvoked += this.OnMenuCommandInvoked;
        this.view.Visible = true;
    }

    public void Dispose()
    {
        if (this.disposed)
        {
            return;
        }

        this.disposed = true;
        this.view.MouseClicked -= this.OnMouseClicked;
        this.view.MenuCommandInvoked -= this.OnMenuCommandInvoked;
        this.view.Visible = false;
        this.view.Dispose();
    }

    private void OnMouseClicked(TrayMouseButton button)
    {
        if (button == TrayMouseButton.Left)
        {
            this.openOptions();
        }
    }

    private void OnMenuCommandInvoked(TrayMenuCommand command)
    {
        switch (command)
        {
            case TrayMenuCommand.Options:
                this.openOptions();
                break;
            case TrayMenuCommand.Exit:
                this.exit();
                break;
        }
    }
}
