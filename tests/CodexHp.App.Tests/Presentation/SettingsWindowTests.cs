using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using CodexHp.App.Infrastructure;
using CodexHp.App.Presentation;
using CodexHp.App.Presentation.Settings;
using CodexHp.Core.Positioning;
using CodexHp.Core.Settings;
using Xunit;

namespace CodexHp.App.Tests.Presentation;

public sealed class SettingsWindowTests
{
    [Fact]
    public void Window_is_resizable_scrollable_and_uses_logical_appearance_units() =>
        StaTest.Run(() =>
    {
        var window = CreateWindow(SettingsGroupKind.Appearance);
        try
        {
            window.Show();
            PumpDispatcher();

            Assert.Equal(ResizeMode.CanResizeWithGrip, window.ResizeMode);
            Assert.Equal(WindowStartupLocation.Manual, window.WindowStartupLocation);
            Assert.True(window.MinWidth <= 480);
            Assert.True(window.MinHeight <= 360);
            var scroller = Assert.IsType<ScrollViewer>(window.FindName("SettingsContentScroller"));
            Assert.Equal(ScrollBarVisibility.Auto, scroller.VerticalScrollBarVisibility);
            Assert.Equal(ScrollBarVisibility.Auto, scroller.HorizontalScrollBarVisibility);
            var unitLabels = FindLogicalDescendants<TextBlock>(window)
                .Where(textBlock => textBlock.IsVisible && textBlock.Text == "DIP")
                .ToArray();
            Assert.Equal(6, unitLabels.Length);
        }
        finally
        {
            window.Close();
            PumpDispatcher();
        }
    });

    [Theory]
    [InlineData(0, 0, 1000, 700, 2, 0, 0, 1000, 700)]
    [InlineData(-1920, 0, 1920, 1040, 1.5, -1448, 143, 975, 753)]
    public void Initial_window_placement_is_centered_and_clamped_to_the_monitor_work_area(
        int workLeft,
        int workTop,
        int workWidth,
        int workHeight,
        double scale,
        int expectedLeft,
        int expectedTop,
        int expectedWidth,
        int expectedHeight)
    {
        var placement = SettingsWindowPlacementCalculator.Resolve(
            new PhysicalRect(workLeft, workTop, workWidth, workHeight),
            scale,
            scale,
            desiredWidthDip: 650,
            desiredHeightDip: 502);

        Assert.Equal(
            new PhysicalRect(expectedLeft, expectedTop, expectedWidth, expectedHeight),
            placement);
    }

    [Fact]
    public void About_page_is_last_and_displays_the_running_binary_version() =>
        StaTest.Run(() =>
    {
        var viewModel = new SettingsWindowViewModel(
            AppSettings.Default,
            _ => { },
            _ => { },
            settings => settings);
        Assert.Equal("About", viewModel.Groups[^1].Title);
        viewModel.SelectedGroup = viewModel.Groups[^1];
        var window = new SettingsWindow(viewModel);
        try
        {
            window.Show();
            PumpDispatcher();

            var version = Assert.IsType<TextBlock>(window.FindName("AboutVersionText"));
            var commit = Assert.IsType<TextBlock>(window.FindName("AboutCommitText"));
            var developer = Assert.IsType<TextBlock>(window.FindName("AboutDeveloperEmailText"));
            Assert.True(version.IsVisible);
            Assert.True(commit.IsVisible);
            Assert.True(developer.IsVisible);
            Assert.Equal("Version 0.3.1", version.Text);
            Assert.Matches("^Commit [0-9a-f]{40}$", commit.Text);
            Assert.Equal("Developer: netics01@gmail.com", developer.Text);
            var buildDetails = Assert.IsType<StackPanel>(LogicalTreeHelper.GetParent(version));
            Assert.Same(buildDetails, LogicalTreeHelper.GetParent(commit));
            Assert.Same(buildDetails, LogicalTreeHelper.GetParent(developer));
            Assert.Equal(Orientation.Vertical, buildDetails.Orientation);
            Assert.Same(
                window.TryFindResource("TextFillColorSecondaryBrush"),
                version.Foreground);
            Assert.Same(
                window.TryFindResource("TextFillColorSecondaryBrush"),
                commit.Foreground);
            Assert.Same(
                window.TryFindResource("TextFillColorSecondaryBrush"),
                developer.Foreground);
            HoldForVisualProbe();
        }
        finally
        {
            window.Close();
            PumpDispatcher();
        }
    });

    [Fact]
    public void About_page_exposes_the_requested_clickable_repository_url() =>
        StaTest.Run(() =>
    {
        var viewModel = new SettingsWindowViewModel(
            AppSettings.Default,
            _ => { },
            _ => { },
            settings => settings);
        viewModel.SelectedGroup = viewModel.Groups[^1];
        var window = new SettingsWindow(viewModel);
        try
        {
            window.Show();
            PumpDispatcher();

            var link = Assert.IsType<Hyperlink>(window.FindName("AboutRepositoryLink"));
            var expectedUri = new Uri("https://github.com/netics01/codexhp");
            Assert.True(link.IsEnabled);
            Assert.Equal(expectedUri, link.NavigateUri);
            Assert.Equal(expectedUri.AbsoluteUri, new TextRange(link.ContentStart, link.ContentEnd).Text);
        }
        finally
        {
            window.Close();
            PumpDispatcher();
        }
    });

    [Fact]
    public void Colors_page_labels_explain_the_UI_concept_and_its_actual_meaning() =>
        StaTest.Run(() =>
    {
        var window = CreateWindow(SettingsGroupKind.Color);
        try
        {
            window.Show();
            PumpDispatcher();

            var visibleText = FindLogicalDescendants<TextBlock>(window)
                .Where(textBlock => textBlock.IsVisible)
                .Select(textBlock => textBlock.Text)
                .ToArray();
            var expectedLabels = new[]
            {
                "ManaBar: Token Limit Gauge for 5 Hours",
                "HpBar: Token Limit Gauge for One Week",
                "Refresh Gauge: Time Remaining Until Token Limit Reset",
                "Issue Stripe: OpenAI Service Issue Detected",
                "Unknown Stripe: Unable to Check OpenAI Service Status",
                "Token Graph Low: ≤10K Tokens per 15s",
                "Token Graph High: ≥100K Tokens per 15s",
            };

            Assert.All(expectedLabels, label => Assert.Contains(label, visibleText));
        }
        finally
        {
            window.Close();
            PumpDispatcher();
        }
    });

    [Fact]
    public void Colors_page_uses_button_height_swatches_and_compact_pick_buttons() =>
        StaTest.Run(() =>
    {
        var window = CreateWindow(SettingsGroupKind.Color);
        try
        {
            window.Show();
            PumpDispatcher();

            var swatches = new[]
            {
                "ManaColorSwatch",
                "HpColorSwatch",
                "RefreshColorSwatch",
                "IssueColorSwatch",
                "UnknownColorSwatch",
                "TokenLowColorSwatch",
                "TokenHighColorSwatch",
            }.Select(name => Assert.IsType<Border>(window.FindName(name))).ToArray();
            var visibleButtons = FindLogicalDescendants<Button>(window)
                .Where(button => button.IsVisible)
                .ToArray();
            var pickButtons = visibleButtons
                .Where(button => Equals(button.Content, "Pick"))
                .ToArray();

            Assert.All(swatches, swatch => Assert.Equal(24, swatch.ActualHeight));
            Assert.Equal(7, pickButtons.Length);
            Assert.All(pickButtons, button =>
            {
                Assert.Equal(24, button.ActualHeight);
                Assert.Equal(44, button.ActualWidth);
            });
            Assert.DoesNotContain(visibleButtons, button => Equals(button.Content, "Choose"));
        }
        finally
        {
            window.Close();
            PumpDispatcher();
        }
    });

    [Fact]
    public void Appearance_text_boxes_use_visible_fluent_colors_and_show_their_values() =>
        StaTest.Run(() =>
    {
        var window = CreateWindow(SettingsGroupKind.Appearance);
        try
        {
            window.Show();
            PumpDispatcher();

            var textBoxes = FindLogicalDescendants<TextBox>(window)
                .Where(textBox => textBox.IsVisible)
                .ToArray();
            var controlBackground = Assert.IsAssignableFrom<Brush>(
                window.TryFindResource("ControlFillColorDefaultBrush"));
            var primaryText = Assert.IsAssignableFrom<Brush>(
                window.TryFindResource("TextFillColorPrimaryBrush"));

            Assert.Equal(6, textBoxes.Length);
            Assert.All(textBoxes, textBox =>
            {
                Assert.Same(controlBackground, textBox.Background);
                Assert.Same(primaryText, textBox.Foreground);
                Assert.False(string.IsNullOrWhiteSpace(textBox.Text));
            });
            HoldForVisualProbe();
        }
        finally
        {
            window.Close();
            PumpDispatcher();
        }
    });

    [Fact]
    public void Appearance_page_shows_visible_history_and_uses_compact_numeric_width() =>
        StaTest.Run(() =>
    {
        var window = CreateWindow(SettingsGroupKind.Appearance);
        try
        {
            window.Show();
            PumpDispatcher();

            var valuesGrid = Assert.IsType<Grid>(window.FindName("AppearanceValuesGrid"));
            Assert.Equal(new GridLength(48), valuesGrid.ColumnDefinitions[1].Width);

            var history = Assert.IsType<TextBlock>(window.FindName("VisibleTokenHistoryText"));
            Assert.Equal("Visible token history: 21 min 0 sec", history.Text);
            Assert.Equal(6, Grid.GetRow(history));
            Assert.Equal(3, Grid.GetColumnSpan(history));
            HoldForVisualProbe();
        }
        finally
        {
            window.Close();
            PumpDispatcher();
        }
    });

    [Fact]
    public void Appearance_text_boxes_use_the_theme_text_color_for_the_caret() =>
        StaTest.Run(() =>
    {
        var window = CreateWindow(SettingsGroupKind.Appearance);
        try
        {
            window.Show();
            PumpDispatcher();

            var textBoxes = FindLogicalDescendants<TextBox>(window)
                .Where(textBox => textBox.IsVisible)
                .ToArray();
            var primaryText = Assert.IsAssignableFrom<Brush>(
                window.TryFindResource("TextFillColorPrimaryBrush"));

            Assert.Equal(6, textBoxes.Length);
            Assert.All(textBoxes, textBox => Assert.Same(primaryText, textBox.CaretBrush));

            var focusedTextBox = textBoxes[0];
            Assert.True(focusedTextBox.Focus());
            focusedTextBox.CaretIndex = focusedTextBox.Text.Length;
            PumpDispatcher();
            HoldForVisualProbe();
        }
        finally
        {
            window.Close();
            PumpDispatcher();
        }
    });

    [Fact]
    public void Appearance_text_edit_is_deferred_until_focus_leaves_the_field() =>
        StaTest.Run(() =>
    {
        var previews = new List<AppSettings>();
        var viewModel = new SettingsWindowViewModel(
            AppSettings.Default,
            previews.Add,
            _ => { },
            settings => settings);
        viewModel.SelectedGroup = viewModel.Groups.Single(group => group.Kind == SettingsGroupKind.Appearance);
        var window = new SettingsWindow(viewModel);
        try
        {
            window.Show();
            PumpDispatcher();

            var widthTextBox = FindLogicalDescendants<TextBox>(window)
                .First(textBox => textBox.IsVisible);
            Assert.True(widthTextBox.Focus());
            widthTextBox.Text = "420";
            PumpDispatcher();

            Assert.Equal(AppSettings.Default.Appearance.OverlayWidth, viewModel.Working.Appearance.OverlayWidth);
            Assert.Empty(previews);

            var resetButton = Assert.IsType<Button>(window.FindName("ResetAppearanceButton"));
            Assert.True(resetButton.Focus());
            PumpDispatcher();

            Assert.Equal(420, viewModel.Working.Appearance.OverlayWidth);
            Assert.Equal(420, Assert.Single(previews).Appearance.OverlayWidth);
        }
        finally
        {
            window.Close();
            PumpDispatcher();
        }
    });

    [Fact]
    public void Appearance_text_edit_is_applied_when_enter_is_pressed() =>
        StaTest.Run(() =>
    {
        var previews = new List<AppSettings>();
        var viewModel = new SettingsWindowViewModel(
            AppSettings.Default,
            previews.Add,
            _ => { },
            settings => settings);
        viewModel.SelectedGroup = viewModel.Groups.Single(group => group.Kind == SettingsGroupKind.Appearance);
        var window = new SettingsWindow(viewModel);
        try
        {
            window.Show();
            PumpDispatcher();

            var widthTextBox = FindLogicalDescendants<TextBox>(window)
                .First(textBox => textBox.IsVisible);
            Assert.True(widthTextBox.Focus());
            widthTextBox.Text = "421";
            PumpDispatcher();
            Assert.Equal(AppSettings.Default.Appearance.OverlayWidth, viewModel.Working.Appearance.OverlayWidth);
            Assert.Empty(previews);

            var inputSource = PresentationSource.FromVisual(window);
            Assert.NotNull(inputSource);
            var enter = new System.Windows.Input.KeyEventArgs(
                System.Windows.Input.Keyboard.PrimaryDevice,
                inputSource!,
                Environment.TickCount,
                System.Windows.Input.Key.Enter)
            {
                RoutedEvent = System.Windows.Input.Keyboard.PreviewKeyDownEvent,
                Source = widthTextBox,
            };
            widthTextBox.RaiseEvent(enter);
            PumpDispatcher();

            Assert.True(enter.Handled);
            Assert.True(widthTextBox.IsKeyboardFocusWithin);
            Assert.Equal(421, viewModel.Working.Appearance.OverlayWidth);
            Assert.Equal(421, Assert.Single(previews).Appearance.OverlayWidth);
        }
        finally
        {
            window.Close();
            PumpDispatcher();
        }
    });

    [Fact]
    public void Appearance_size_units_use_device_independent_pixels() =>
        StaTest.Run(() =>
    {
        var window = CreateWindow(SettingsGroupKind.Appearance);
        try
        {
            window.Show();
            PumpDispatcher();

            var visibleText = FindLogicalDescendants<TextBlock>(window)
                .Where(textBlock => textBlock.IsVisible)
                .Select(textBlock => textBlock.Text)
                .ToArray();

            Assert.Equal(6, visibleText.Count(text => text == "DIP"));
            Assert.DoesNotContain("px", visibleText);
            Assert.DoesNotContain("Physical px", visibleText);
        }
        finally
        {
            window.Close();
            PumpDispatcher();
        }
    });

    [Fact]
    public void Appearance_page_reset_button_restores_only_default_appearance_and_previews_it() =>
        StaTest.Run(() =>
    {
        var baseline = AppSettings.Default with
        {
            StartWithWindows = false,
            ShowOnlyWhenChatGptRunning = true,
            Colors = AppSettings.Default.Colors with { ManaBar = ColorValue.Parse("#010101") },
            Appearance = AppSettings.Default.Appearance with
            {
                OverlayWidth = 420,
                OverlayHeight = 80,
                GaugePaneWidth = 120,
                GraphBarWidth = 3,
                GraphBarGap = 1,
                StatusStripeWidth = 5,
            },
            Location = new OverlayLocationSettings("DISPLAY2", 10, 20),
        };
        var previews = new List<AppSettings>();
        var viewModel = new SettingsWindowViewModel(
            baseline,
            previews.Add,
            _ => { },
            settings => settings);
        viewModel.SelectedGroup = viewModel.Groups.Single(group => group.Kind == SettingsGroupKind.Appearance);
        var window = new SettingsWindow(viewModel);
        try
        {
            window.Show();
            PumpDispatcher();

            var resetButton = Assert.IsType<Button>(window.FindName("ResetAppearanceButton"));
            Assert.Equal("Reset to Defaults", resetButton.Content);
            Assert.Equal(1, Grid.GetRow(resetButton));
            Assert.Equal(VerticalAlignment.Bottom, resetButton.VerticalAlignment);

            resetButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            PumpDispatcher();

            var expected = baseline with { Appearance = AppearanceSettings.Default };
            Assert.Equal(expected, viewModel.Working);
            Assert.Equal(expected, Assert.Single(previews));
            HoldForVisualProbe();
        }
        finally
        {
            window.Close();
            PumpDispatcher();
        }
    });

    [Fact]
    public void General_page_does_not_show_the_redundant_apply_hint() =>
        StaTest.Run(() =>
    {
        var window = CreateWindow();
        try
        {
            window.Show();
            PumpDispatcher();

            var visibleText = FindLogicalDescendants<TextBlock>(window)
                .Select(textBlock => textBlock.Text)
                .ToArray();

            Assert.DoesNotContain(
                "General settings take effect after you select OK. The default is Always show.",
                visibleText);
        }
        finally
        {
            window.Close();
            PumpDispatcher();
        }
    });

    [Fact]
    public void Colors_page_reset_button_restores_only_default_colors_and_previews_them() =>
        StaTest.Run(() =>
    {
        var customizedColors = new ColorSettings(
            ColorValue.Parse("#010101"),
            ColorValue.Parse("#020202"),
            ColorValue.Parse("#030303"),
            ColorValue.Parse("#040404"),
            ColorValue.Parse("#050505"),
            ColorValue.Parse("#060606"),
            ColorValue.Parse("#070707"));
        var baseline = AppSettings.Default with
        {
            StartWithWindows = false,
            ShowOnlyWhenChatGptRunning = true,
            Colors = customizedColors,
            Appearance = AppSettings.Default.Appearance with { OverlayWidth = 420 },
            Location = new OverlayLocationSettings("DISPLAY2", 10, 20),
        };
        var previews = new List<AppSettings>();
        var viewModel = new SettingsWindowViewModel(
            baseline,
            previews.Add,
            _ => { },
            settings => settings);
        viewModel.SelectedGroup = viewModel.Groups.Single(group => group.Kind == SettingsGroupKind.Color);
        var window = new SettingsWindow(viewModel);
        try
        {
            window.Show();
            PumpDispatcher();

            var resetButton = Assert.IsType<Button>(window.FindName("ResetColorsButton"));
            Assert.Equal("Reset to Defaults", resetButton.Content);
            Assert.Equal(1, Grid.GetRow(resetButton));
            Assert.Equal(VerticalAlignment.Bottom, resetButton.VerticalAlignment);

            resetButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            PumpDispatcher();

            var expected = baseline with { Colors = ColorSettings.Default };
            Assert.Equal(expected, viewModel.Working);
            Assert.Equal(expected, Assert.Single(previews));
            HoldForVisualProbe();
        }
        finally
        {
            window.Close();
            PumpDispatcher();
        }
    });

    [Fact]
    public void Custom_settings_surfaces_use_the_active_fluent_theme_resources() =>
        StaTest.Run(() =>
    {
        var window = CreateWindow();
        try
        {
            window.Show();
            PumpDispatcher();

            var navigation = Assert.IsType<ListBox>(window.FindName("GroupList"));
            var generalHeader = Assert.IsType<Border>(window.FindName("GeneralHeader"));
            var generalBody = Assert.IsType<Border>(window.FindName("GeneralBody"));
            var okButton = Assert.IsType<Button>(window.FindName("OkButton"));
            var startWithWindows = FindContentControl<CheckBox>(
                window,
                "Run CodexHp when Windows starts");

            var themeModeProperty = typeof(Window).GetProperty("ThemeMode");
            Assert.NotNull(themeModeProperty);
            Assert.Equal("System", themeModeProperty!.GetValue(window)?.ToString());

            var applicationBackground = Assert.IsAssignableFrom<Brush>(
                window.TryFindResource("ApplicationBackgroundBrush"));
            var navigationBackground = Assert.IsAssignableFrom<Brush>(
                window.TryFindResource("SolidBackgroundFillColorSecondaryBrush"));
            var headerBackground = Assert.IsAssignableFrom<Brush>(
                window.TryFindResource("ControlSolidFillColorDefaultBrush"));
            var controlBackground = Assert.IsAssignableFrom<Brush>(
                window.TryFindResource("ControlFillColorDefaultBrush"));
            var primaryText = Assert.IsAssignableFrom<Brush>(
                window.TryFindResource("TextFillColorPrimaryBrush"));
            var selectedNavigation = Assert.IsType<SolidColorBrush>(
                window.TryFindResource("SettingsSelectedBrush"));

            Assert.Same(applicationBackground, window.Background);
            Assert.Same(primaryText, window.Foreground);
            Assert.Same(navigationBackground, navigation.Background);
            Assert.Same(primaryText, navigation.Foreground);
            Assert.Same(headerBackground, generalHeader.Background);
            Assert.Same(applicationBackground, generalBody.Background);
            Assert.Same(controlBackground, okButton.Background);
            Assert.Same(primaryText, okButton.Foreground);
            Assert.Same(primaryText, startWithWindows.Foreground);
            Assert.Equal(0.44, selectedNavigation.Opacity, 2);

            var selectedNavigationItem = Assert.IsType<ListBoxItem>(
                navigation.ItemContainerGenerator.ContainerFromIndex(0));
            var selectedNavigationBorder = Assert.IsType<Border>(
                VisualTreeHelper.GetChild(selectedNavigationItem, 0));
            Assert.Same(selectedNavigation, selectedNavigationBorder.Background);

            var unselectedNavigationItem = Assert.IsType<ListBoxItem>(
                navigation.ItemContainerGenerator.ContainerFromIndex(1));
            var foregroundSource = DependencyPropertyHelper.GetValueSource(
                unselectedNavigationItem,
                Control.ForegroundProperty);
            Assert.Equal(BaseValueSource.Inherited, foregroundSource.BaseValueSource);
        }
        finally
        {
            window.Close();
            PumpDispatcher();
        }
    });

    [Fact]
    public void Navigation_and_page_content_share_the_same_vertical_bounds() =>
        StaTest.Run(() =>
    {
        var window = CreateWindow();
        try
        {
            window.Show();
            PumpDispatcher();

            var root = Assert.IsType<Grid>(window.FindName("SettingsRoot"));
            var navigation = Assert.IsType<ListBox>(window.FindName("GroupList"));
            var generalPanel = Assert.IsType<Grid>(window.FindName("GeneralPanel"));
            var pageHost = Assert.IsType<Grid>(LogicalTreeHelper.GetParent(generalPanel));

            var navigationTop = navigation.TranslatePoint(new Point(0, 0), root).Y;
            var pageTop = pageHost.TranslatePoint(new Point(0, 0), root).Y;
            Assert.Equal(navigationTop, pageTop, 3);
            Assert.Equal(navigation.ActualHeight, pageHost.ActualHeight, 3);
        }
        finally
        {
            window.Close();
            PumpDispatcher();
        }
    });

    [Fact]
    public void Picpick_reference_contract_uses_compact_settings_layout() =>
        StaTest.Run(() =>
    {
        var window = CreateWindow();
        try
        {
            window.Show();
            PumpDispatcher();
            HoldForVisualProbe();

            Assert.Equal(650, window.Width);
            Assert.Equal(502, window.Height);
            Assert.Equal(480, window.MinWidth);
            Assert.Equal(360, window.MinHeight);
            Assert.Equal(ResizeMode.CanResizeWithGrip, window.ResizeMode);
            Assert.Equal(13, window.FontSize);

            var root = Assert.IsType<Grid>(window.FindName("SettingsRoot"));
            Assert.Equal(new Thickness(10), root.Margin);

            var navigation = Assert.IsType<ListBox>(window.FindName("GroupList"));
            Assert.Equal(130, navigation.Width);
            Assert.Equal(13, navigation.FontSize);
            Assert.Equal(new Thickness(0.5), navigation.BorderThickness);
            var firstNavigationItem = Assert.IsType<ListBoxItem>(
                navigation.ItemContainerGenerator.ContainerFromIndex(0));
            Assert.Equal(30, firstNavigationItem.ActualHeight);

            var generalHeader = Assert.IsType<Border>(window.FindName("GeneralHeader"));
            Assert.Equal(28, generalHeader.Height);
            var generalTitle = Assert.IsType<TextBlock>(window.FindName("GeneralSectionTitle"));
            Assert.Equal(14, generalTitle.FontSize);
            Assert.Equal(FontWeights.SemiBold, generalTitle.FontWeight);
            var generalBody = Assert.IsType<Border>(window.FindName("GeneralBody"));
            Assert.Equal(new Thickness(10), generalBody.Padding);
            Assert.Equal(new Thickness(0.5), generalBody.BorderThickness);

            var okButton = Assert.IsType<Button>(window.FindName("OkButton"));
            var cancelButton = Assert.IsType<Button>(window.FindName("CancelButton"));
            Assert.Equal(92, okButton.Width);
            Assert.Equal(26, okButton.Height);
            Assert.Equal(92, cancelButton.Width);
            Assert.Equal(26, cancelButton.Height);
        }
        finally
        {
            window.Close();
            PumpDispatcher();
        }
    });

    [Fact]
    public void Alt_tab_hidden_style_adds_tool_window_and_removes_app_window()
    {
        var style = AltTabWindowStyle.BuildExtendedStyle(
            NativeMethods.WsExAppWindow | NativeMethods.WsExTopmost);

        Assert.NotEqual(0u, style & NativeMethods.WsExToolWindow);
        Assert.Equal(0u, style & NativeMethods.WsExAppWindow);
        Assert.NotEqual(0u, style & NativeMethods.WsExTopmost);
    }

    [Fact]
    public void Alt_tab_visible_style_removes_tool_window_and_adds_app_window()
    {
        var style = AltTabWindowStyle.BuildVisibleExtendedStyle(
            NativeMethods.WsExToolWindow | NativeMethods.WsExTopmost);

        Assert.Equal(0u, style & NativeMethods.WsExToolWindow);
        Assert.NotEqual(0u, style & NativeMethods.WsExAppWindow);
        Assert.NotEqual(0u, style & NativeMethods.WsExTopmost);
    }

    [Fact]
    public void Visible_settings_window_is_included_in_alt_tab() =>
        StaTest.Run(() =>
    {
        var viewModel = new SettingsWindowViewModel(
            AppSettings.Default,
            _ => { },
            _ => { },
            settings => settings);
        var window = new SettingsWindow(viewModel);
        try
        {
            window.Show();
            PumpDispatcher();

            var windowHandle = new WindowInteropHelper(window).Handle;
            var extendedStyle = unchecked((uint)NativeMethods.GetWindowLongPointer(
                windowHandle,
                NativeMethods.GwlExStyle).ToInt64());

            Assert.True(window.ShowInTaskbar);
            Assert.Equal(0u, extendedStyle & NativeMethods.WsExToolWindow);
            Assert.NotEqual(0u, extendedStyle & NativeMethods.WsExAppWindow);
        }
        finally
        {
            window.Close();
            PumpDispatcher();
        }
    });

    [Fact]
    public void Program_owned_settings_and_error_text_is_English() =>
        StaTest.Run(() =>
    {
        var viewModel = new SettingsWindowViewModel(
            AppSettings.Default,
            _ => { },
            _ => { },
            settings => settings);
        var window = new SettingsWindow(viewModel);
        try
        {
            var userFacingText = ReadUserFacingText(window).ToArray();

            Assert.Equal("CodexHp Settings", window.Title);
            Assert.Contains("General", userFacingText);
            Assert.Contains("Show the usage overlay only while the ChatGPT desktop app is running", userFacingText);
            Assert.Contains("Usage Overlay Width", userFacingText);
            Assert.Contains("Usage Overlay Height", userFacingText);
            Assert.Contains("Overlay Position", userFacingText);
            Assert.Contains("Drag the usage overlay to the desired location.", userFacingText);
            Assert.DoesNotContain(
                userFacingText,
                value => value.Contains("screen area", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(
                userFacingText,
                value => Regex.IsMatch(value, "[가-힣]", RegexOptions.CultureInvariant));
            Assert.Equal("CodexHp could not start.", UserInterfaceText.StartupFailure);
            Assert.Equal("Settings could not be saved.", UserInterfaceText.SettingsSaveFailure);
        }
        finally
        {
            window.Close();
            PumpDispatcher();
        }
    });

    [Fact]
    public void System_close_cancels_once_and_closes_without_reentrancy() =>
        StaTest.Run(() =>
    {
        var previewCount = 0;
        var viewModel = new SettingsWindowViewModel(
            AppSettings.Default,
            _ => previewCount++,
            _ => { },
            settings => settings);
        var window = new SettingsWindow(viewModel);
        var closedCount = 0;
        window.Closed += (_, _) => closedCount++;

        window.Show();
        window.Close();
        PumpDispatcher();

        Assert.True(viewModel.IsClosed);
        Assert.Equal(1, previewCount);
        Assert.Equal(1, closedCount);
    });

    private static void PumpDispatcher()
    {
        var frame = new DispatcherFrame();
        _ = Dispatcher.CurrentDispatcher.BeginInvoke(
            DispatcherPriority.ApplicationIdle,
            new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }

    private static SettingsWindow CreateWindow(SettingsGroupKind group = SettingsGroupKind.General)
    {
        var viewModel = new SettingsWindowViewModel(
            AppSettings.Default,
            _ => { },
            _ => { },
            settings => settings);
        viewModel.SelectedGroup = viewModel.Groups.Single(item => item.Kind == group);
        return new SettingsWindow(viewModel);
    }

    private static T FindContentControl<T>(DependencyObject root, string content)
        where T : ContentControl
    {
        if (root is T { Content: string value } control && value == content)
        {
            return control;
        }

        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        {
            try
            {
                return FindContentControl<T>(child, content);
            }
            catch (InvalidOperationException)
            {
            }
        }

        throw new InvalidOperationException($"The content control '{content}' was not found.");
    }

    private static IEnumerable<T> FindLogicalDescendants<T>(DependencyObject root)
        where T : DependencyObject
    {
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        {
            if (child is T match)
            {
                yield return match;
            }

            foreach (var descendant in FindLogicalDescendants<T>(child))
            {
                yield return descendant;
            }
        }
    }

    private static void HoldForVisualProbe()
    {
        var rawDuration = Environment.GetEnvironmentVariable("CODEXHP_SETTINGS_VISUAL_HOLD_MS");
        if (!int.TryParse(rawDuration, out var durationMilliseconds) || durationMilliseconds <= 0)
        {
            return;
        }

        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(
            TimeSpan.FromMilliseconds(durationMilliseconds),
            DispatcherPriority.ApplicationIdle,
            (_, _) => frame.Continue = false,
            Dispatcher.CurrentDispatcher);
        timer.Start();
        Dispatcher.PushFrame(frame);
        timer.Stop();
    }

    private static IEnumerable<string> ReadUserFacingText(DependencyObject root)
    {
        if (root is System.Windows.Controls.TextBlock { Text: { Length: > 0 } text })
        {
            yield return text;
        }

        if (root is System.Windows.Controls.ContentControl { Content: string content })
        {
            yield return content;
        }

        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        {
            foreach (var value in ReadUserFacingText(child))
            {
                yield return value;
            }
        }
    }
}
