using CodexHp.App.Application;

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
    Repository,
    Update,
}

public enum TrayIconAsset
{
    CodexHpGauge,
}

public sealed record TrayMenuItem(TrayMenuCommand Command, string Text, bool SeparatorBefore = false);

internal static class TrayIconMessageRouter
{
    private const uint LeftButtonUp = 0x0202;
    private const uint RightButtonUp = 0x0205;
    private const uint OptionsCommandId = 1;
    private const uint ExitCommandId = 2;
    private const uint RepositoryCommandId = 3;

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
        RepositoryCommandId => TrayMenuCommand.Repository,
        4 => TrayMenuCommand.Update,
        _ => null,
    };
}

public interface ITrayIconView : IDisposable
{
    event Action<TrayMouseButton>? MouseClicked;

    event Action<TrayMenuCommand>? MenuCommandInvoked;

    bool Visible { get; set; }

    TrayIconAsset IconAsset { get; }

    string ToolTipText { get; set; }

    IReadOnlyList<TrayMenuItem> MenuItems { get; set; }
}

public sealed class TrayIconController : IDisposable
{
    public static IReadOnlyList<TrayMenuItem> DefaultMenuItems { get; } =
    [
        new TrayMenuItem(TrayMenuCommand.Options, "Settings"),
        new TrayMenuItem(TrayMenuCommand.Repository, "Open GitHub"),
        new TrayMenuItem(TrayMenuCommand.Exit, "Exit", SeparatorBefore: true),
    ];

    private readonly ITrayIconView view;
    private readonly Action openOptions;
    private readonly Action exit;
    private readonly Action openRepository;
    private readonly Action<AvailableUpdate> openUpdate;
    private AvailableUpdate? availableUpdate;
    private bool disposed;

    public TrayIconController(Action openOptions, Action exit)
        : this(new WindowsTrayIconView(), openOptions, exit)
    {
    }

    public TrayIconController(ITrayIconView view, Action openOptions, Action exit, Action? openRepository = null,
        Action<AvailableUpdate>? openUpdate = null)
    {
        this.view = view ?? throw new ArgumentNullException(nameof(view));
        this.openOptions = openOptions ?? throw new ArgumentNullException(nameof(openOptions));
        this.exit = exit ?? throw new ArgumentNullException(nameof(exit));
        this.openRepository = openRepository ?? RepositoryBrowser.Open;
        this.openUpdate = openUpdate ?? ReleaseBrowser.Open;
        this.view.MouseClicked += this.OnMouseClicked;
        this.view.MenuCommandInvoked += this.OnMenuCommandInvoked;
        this.view.Visible = true;
    }

    public void SetStatusMessage(string? message)
    {
        ObjectDisposedException.ThrowIf(this.disposed, this);
        this.view.ToolTipText = string.IsNullOrWhiteSpace(message)
            ? "CodexHp"
            : $"CodexHp — {message.Trim()}";
    }

    public void SetAvailableUpdate(AvailableUpdate? update)
    {
        ObjectDisposedException.ThrowIf(this.disposed, this);
        if (this.availableUpdate == update) return;
        this.availableUpdate = update;
        this.view.MenuItems = update is null ? DefaultMenuItems :
        [DefaultMenuItems[0], DefaultMenuItems[1], new(TrayMenuCommand.Update, "Update available"), DefaultMenuItems[2]];
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
            case TrayMenuCommand.Repository:
                this.openRepository();
                break;
            case TrayMenuCommand.Update when this.availableUpdate is { } update:
                this.openUpdate(update);
                break;
        }
    }
}
