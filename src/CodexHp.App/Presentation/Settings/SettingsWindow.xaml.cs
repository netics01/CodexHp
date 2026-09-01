using System.ComponentModel;
using System.Diagnostics;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Navigation;
using CodexHp.App.Infrastructure;
using CodexHp.Core.Positioning;
using CodexHp.Core.Settings;

namespace CodexHp.App.Presentation.Settings;

public partial class SettingsWindow : System.Windows.Window
{
    private readonly SettingsWindowViewModel viewModel;
    private readonly IColorPicker colorPicker;
    private bool closeAuthorized;

    public SettingsWindow(SettingsWindowViewModel viewModel)
        : this(viewModel, new Win32ColorPicker())
    {
    }

    internal SettingsWindow(SettingsWindowViewModel viewModel, IColorPicker colorPicker)
    {
        this.viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        this.colorPicker = colorPicker ?? throw new ArgumentNullException(nameof(colorPicker));
        this.InitializeComponent();
        this.DataContext = viewModel;
        this.viewModel.CloseRequested += this.OnViewModelCloseRequested;
        this.viewModel.PropertyChanged += this.OnViewModelPropertyChanged;
        this.ShowGroup(viewModel.SelectedGroup.Kind);
        this.RefreshColorSwatches();
    }

    protected override void OnSourceInitialized(EventArgs eventArgs)
    {
        base.OnSourceInitialized(eventArgs);
        AltTabWindowStyle.ApplyVisible(new WindowInteropHelper(this).Handle);
    }

    internal void ConstrainToWorkArea(MonitorGeometry monitor, bool center = true)
    {
        ArgumentNullException.ThrowIfNull(monitor);
        var scaleX = double.IsFinite(monitor.ScaleX) && monitor.ScaleX > 0 ? monitor.ScaleX : 1;
        var scaleY = double.IsFinite(monitor.ScaleY) && monitor.ScaleY > 0 ? monitor.ScaleY : 1;
        var maximumWidthDip = monitor.WorkArea.Width / scaleX;
        var maximumHeightDip = monitor.WorkArea.Height / scaleY;
        this.MinWidth = Math.Min(480, maximumWidthDip);
        this.MinHeight = Math.Min(360, maximumHeightDip);
        this.MaxWidth = maximumWidthDip;
        this.MaxHeight = maximumHeightDip;
        var desiredWidth = double.IsFinite(this.ActualWidth) && this.ActualWidth > 0
            ? this.ActualWidth
            : this.Width;
        var desiredHeight = double.IsFinite(this.ActualHeight) && this.ActualHeight > 0
            ? this.ActualHeight
            : this.Height;
        var placement = !center
            && NativeMethods.GetWindowRect(new WindowInteropHelper(this).Handle, out var currentBounds)
                ? ClampToWorkArea(
                    new PhysicalRect(
                        currentBounds.Left,
                        currentBounds.Top,
                        currentBounds.Right - currentBounds.Left,
                        currentBounds.Bottom - currentBounds.Top),
                    monitor.WorkArea)
                : SettingsWindowPlacementCalculator.Resolve(
                    monitor.WorkArea,
                    scaleX,
                    scaleY,
                    desiredWidth,
                    desiredHeight);
        _ = NativeMethods.SetWindowPos(
            new WindowInteropHelper(this).Handle,
            NativeMethods.HwndTop,
            placement.Left,
            placement.Top,
            placement.Width,
            placement.Height,
            NativeMethods.SwpNoActivate);
    }

    private static PhysicalRect ClampToWorkArea(PhysicalRect current, PhysicalRect workArea)
    {
        var width = Math.Min(Math.Max(1, current.Width), workArea.Width);
        var height = Math.Min(Math.Max(1, current.Height), workArea.Height);
        return new PhysicalRect(
            Math.Clamp(current.Left, workArea.Left, workArea.Right - width),
            Math.Clamp(current.Top, workArea.Top, workArea.Bottom - height),
            width,
            height);
    }

    private void OnGroupSelectionChanged(object sender, SelectionChangedEventArgs eventArgs)
    {
        if (eventArgs.AddedItems.Count == 1 && eventArgs.AddedItems[0] is SettingsGroup group)
        {
            this.ShowGroup(group.Kind);
        }
    }

    private void OnRepositoryRequestNavigate(object sender, RequestNavigateEventArgs eventArgs)
    {
        Process.Start(new ProcessStartInfo(eventArgs.Uri.AbsoluteUri)
        {
            UseShellExecute = true,
        });
        eventArgs.Handled = true;
    }

    private void ShowGroup(SettingsGroupKind group)
    {
        this.GeneralPanel.Visibility = group == SettingsGroupKind.General ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
        this.ColorPanel.Visibility = group == SettingsGroupKind.Color ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
        this.AppearancePanel.Visibility = group == SettingsGroupKind.Appearance ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
        this.OverlayPositionPanel.Visibility = group == SettingsGroupKind.OverlayPosition ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
        this.AboutPanel.Visibility = group == SettingsGroupKind.About ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
    }

    private void OnPickManaColor(object sender, System.Windows.RoutedEventArgs eventArgs) =>
        this.PickColor(this.viewModel.ManaBarColor, value => this.viewModel.ManaBarColor = value);

    private void OnPickHpColor(object sender, System.Windows.RoutedEventArgs eventArgs) =>
        this.PickColor(this.viewModel.HpBarColor, value => this.viewModel.HpBarColor = value);

    private void OnPickRefreshColor(object sender, System.Windows.RoutedEventArgs eventArgs) =>
        this.PickColor(this.viewModel.RefreshGaugeColor, value => this.viewModel.RefreshGaugeColor = value);

    private void OnPickIssueColor(object sender, System.Windows.RoutedEventArgs eventArgs) =>
        this.PickColor(this.viewModel.ServiceIssueColor, value => this.viewModel.ServiceIssueColor = value);

    private void OnPickUnknownColor(object sender, System.Windows.RoutedEventArgs eventArgs) =>
        this.PickColor(this.viewModel.ServiceUnknownColor, value => this.viewModel.ServiceUnknownColor = value);

    private void OnPickTokenLowColor(object sender, System.Windows.RoutedEventArgs eventArgs) =>
        this.PickColor(this.viewModel.TokenLowColor, value => this.viewModel.TokenLowColor = value);

    private void OnPickTokenHighColor(object sender, System.Windows.RoutedEventArgs eventArgs) =>
        this.PickColor(this.viewModel.TokenHighColor, value => this.viewModel.TokenHighColor = value);

    private void OnResetColors(object sender, System.Windows.RoutedEventArgs eventArgs)
    {
        this.viewModel.ResetColorsToDefaults();
        this.RefreshColorSwatches();
    }

    private void OnResetAppearance(object sender, System.Windows.RoutedEventArgs eventArgs) =>
        this.viewModel.ResetAppearanceToDefaults();

    private void PickColor(ColorValue current, Action<ColorValue> apply)
    {
        var selected = this.colorPicker.PickColor(new WindowInteropHelper(this).Handle, current);
        if (selected is not { } selectedValue)
        {
            return;
        }

        apply(selectedValue);
        this.RefreshColorSwatches();
    }

    private void RefreshColorSwatches()
    {
        this.ManaColorSwatch.Background = Brush(this.viewModel.ManaBarColor);
        this.HpColorSwatch.Background = Brush(this.viewModel.HpBarColor);
        this.RefreshColorSwatch.Background = Brush(this.viewModel.RefreshGaugeColor);
        this.IssueColorSwatch.Background = Brush(this.viewModel.ServiceIssueColor);
        this.UnknownColorSwatch.Background = Brush(this.viewModel.ServiceUnknownColor);
        this.TokenLowColorSwatch.Background = Brush(this.viewModel.TokenLowColor);
        this.TokenHighColorSwatch.Background = Brush(this.viewModel.TokenHighColor);
    }

    private static SolidColorBrush Brush(ColorValue value) =>
        new(System.Windows.Media.Color.FromRgb(value.Red, value.Green, value.Blue));

    private void OnConfirm(object sender, System.Windows.RoutedEventArgs eventArgs)
    {
        try
        {
            this.viewModel.Confirm();
        }
        catch (Exception exception)
        {
            System.Windows.MessageBox.Show(
                this,
                $"{UserInterfaceText.SettingsSaveFailure}\n\n{exception.Message}",
                "CodexHp",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Error);
        }
    }

    private void OnCancel(object sender, System.Windows.RoutedEventArgs eventArgs) =>
        this.viewModel.Cancel(SettingsCancelTrigger.CancelButton);

    private void OnPreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs eventArgs)
    {
        if (eventArgs.Key == System.Windows.Input.Key.Enter &&
            eventArgs.OriginalSource is System.Windows.Controls.TextBox textBox)
        {
            textBox.GetBindingExpression(System.Windows.Controls.TextBox.TextProperty)?.UpdateSource();
            eventArgs.Handled = true;
            return;
        }

        if (eventArgs.Key != System.Windows.Input.Key.Escape)
        {
            return;
        }

        eventArgs.Handled = true;
        this.viewModel.Cancel(SettingsCancelTrigger.EscapeKey);
    }

    private void OnClosing(object? sender, CancelEventArgs eventArgs)
    {
        if (this.closeAuthorized)
        {
            return;
        }

        eventArgs.Cancel = true;
        _ = this.Dispatcher.BeginInvoke(
            () => this.viewModel.Cancel(SettingsCancelTrigger.WindowClose));
    }

    private void OnViewModelCloseRequested(SettingsCloseRequest request)
    {
        this.closeAuthorized = true;
        this.viewModel.CloseRequested -= this.OnViewModelCloseRequested;
        this.viewModel.PropertyChanged -= this.OnViewModelPropertyChanged;
        this.Close();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs eventArgs)
    {
        if (string.IsNullOrEmpty(eventArgs.PropertyName))
        {
            this.RefreshColorSwatches();
        }
    }
}
