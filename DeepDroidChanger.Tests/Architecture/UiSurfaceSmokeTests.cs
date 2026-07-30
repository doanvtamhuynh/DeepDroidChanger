using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using DeepDroidChanger.Behaviors;
using DeepDroidChanger.Models;
using DeepDroidChanger.Services;
using DeepDroidChanger.ViewModels;
using DeepDroidChanger.Views;
using Microsoft.Extensions.DependencyInjection;

namespace DeepDroidChanger.Tests.Architecture;

[TestClass]
[DoNotParallelize]
public sealed class UiSurfaceSmokeTests
{
    [TestMethod]
    public void AllSurfaces_InstantiateAndMeasure_InBothThemesAndLanguages()
    {
        Exception? failure = null;
        using var completed = new ManualResetEventSlim();
        var thread = new Thread(() =>
        {
            try
            {
                var application = new App();
                application.InitializeComponent();
                var services = new ServiceCollection();
                services.AddLogging();
                App.RegisterServices(services, new AppSettings());
                using ServiceProvider provider = services.BuildServiceProvider(
                    new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
                ILocalizationService localization = provider.GetRequiredService<ILocalizationService>();
                IThemeService themes = provider.GetRequiredService<IThemeService>();
                VerifyMainShellNavigation(provider);
                VerifyInteractiveStyles(provider);
                VerifyEditorRowsAndDataTemplates(provider);

                foreach (string language in new[] { "en", "vi" })
                {
                    localization.ApplyLanguage(language);
                    foreach (string theme in new[] { "Light", "Dark" })
                    {
                        themes.ApplyTheme(theme);
                        MeasureSurface(provider.GetRequiredService<MainWindow>());
                        MeasureSurface(provider.GetRequiredService<DeviceManagerView>());
                        MeasureSurface(provider.GetRequiredService<ChangeMultipleDevicesView>());
                        MeasureSurface(provider.GetRequiredService<SettingsView>());
                        MeasureDialog<LoginDialog, LoginViewModel>(provider);
                        MeasureDialog<AddDevicesDialog, AddDevicesViewModel>(provider);
                        VerifyConfirmationDialog(provider);
                        MeasureDialog<AdvancedChangeConfigDialog, AdvancedChangeConfigViewModel>(provider);
                        VerifyAdvancedChangeConfigDialog(provider);
                        MeasureDialog<RandomDeviceInfoDialog, RandomDeviceInfoViewModel>(provider);
                        VerifyRandomDeviceInfoUpdateButton(provider);
                        MeasureDialog<ChangeLocationDialog, ChangeLocationViewModel>(provider);
                        MeasureDialog<ChangeTimezoneDialog, ChangeTimezoneViewModel>(provider);
                        MeasureDialog<FakeProxyDialog, FakeProxyViewModel>(provider);
                        MeasureDialog<UpdateIntegrityDialog, UpdateIntegrityViewModel>(provider);
                        MeasureDialog<InstallPackageDialog, InstallPackageViewModel>(provider);
                        MeasureDialog<DeviceViewerDialog, DeviceViewerViewModel>(provider);
                    }
                }

                application.Shutdown();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
            finally
            {
                completed.Set();
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        Assert.IsTrue(completed.Wait(TimeSpan.FromSeconds(90)), "WPF surface smoke test timed out.");
        if (failure != null)
            Assert.Fail(failure.ToString());
    }

    private static void MeasureDialog<TDialog, TViewModel>(IServiceProvider provider)
        where TDialog : Window
        where TViewModel : class
    {
        TDialog dialog = provider.GetRequiredService<TDialog>();
        dialog.DataContext = provider.GetRequiredService<TViewModel>();
        MeasureSurface(dialog);
    }

    private static void VerifyMainShellNavigation(IServiceProvider provider)
    {
        MainWindow mainWindow = provider.GetRequiredService<MainWindow>();
        mainWindow.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent, mainWindow));
        MeasureSurface(mainWindow);

        Assert.AreSame(provider.GetRequiredService<MainViewModel>(), mainWindow.DataContext);
        var mainContent = Assert.IsInstanceOfType<ContentControl>(mainWindow.FindName("MainContent"));
        Assert.AreSame(provider.GetRequiredService<DeviceManagerView>(), mainContent.Content);

        var deviceManagerButton = Assert.IsInstanceOfType<Button>(mainWindow.FindName("BtnDeviceManager"));
        Assert.IsNotNull(deviceManagerButton.Command);
        Assert.IsNotNull(deviceManagerButton.ContextMenu);
        Assert.AreEqual(PlacementMode.Right, deviceManagerButton.ContextMenu.Placement);
        Assert.IsFalse(deviceManagerButton.ContextMenu.StaysOpen);
        Assert.HasCount(2, deviceManagerButton.ContextMenu.Items);
        System.Windows.Data.Binding? flyoutBinding = System.Windows.Data.BindingOperations.GetBinding(
            deviceManagerButton,
            ContextMenuOpenBehavior.IsOpenProperty);
        Assert.IsNotNull(flyoutBinding);
        Assert.AreEqual(nameof(MainViewModel.IsDeviceManagerFlyoutOpen), flyoutBinding.Path.Path);
        Assert.AreEqual(System.Windows.Data.BindingMode.TwoWay, flyoutBinding.Mode);
        deviceManagerButton.Command.Execute(null);
        Assert.AreSame(provider.GetRequiredService<DeviceManagerView>(), mainContent.Content);
        Assert.IsTrue(provider.GetRequiredService<MainViewModel>().IsDeviceManagerSubmenuOpen);

        var multipleDevicesButton = Assert.IsInstanceOfType<Button>(
            mainWindow.FindName("BtnChangeMultipleDevices"));
        Assert.IsNotNull(multipleDevicesButton.Command);
        multipleDevicesButton.Command.Execute(null);
        Assert.AreSame(provider.GetRequiredService<ChangeMultipleDevicesView>(), mainContent.Content);

        var singleDeviceButton = Assert.IsInstanceOfType<Button>(
            mainWindow.FindName("BtnChangeSingleDevice"));
        Assert.IsNotNull(singleDeviceButton.Command);
        singleDeviceButton.Command.Execute(null);
        Assert.AreSame(provider.GetRequiredService<DeviceManagerView>(), mainContent.Content);

        var settingsButton = Assert.IsInstanceOfType<Button>(mainWindow.FindName("BtnSettings"));
        Assert.IsNotNull(settingsButton.Command);
        settingsButton.Command.Execute(null);
        Assert.AreSame(provider.GetRequiredService<SettingsView>(), mainContent.Content);

        var toggleButton = Assert.IsInstanceOfType<Button>(mainWindow.FindName("BtnToggle"));
        var sidebarColumn = Assert.IsInstanceOfType<ColumnDefinition>(mainWindow.FindName("SidebarColumn"));
        Assert.IsNotNull(toggleButton.Command);
        toggleButton.Command.Execute(null);
        Assert.AreEqual(56d, sidebarColumn.Width.Value);
        deviceManagerButton.Command.Execute(null);
        Assert.IsTrue(provider.GetRequiredService<MainViewModel>().IsDeviceManagerFlyoutOpen);
        Assert.IsTrue(deviceManagerButton.ContextMenu.IsOpen);
        deviceManagerButton.SetCurrentValue(ContextMenuOpenBehavior.IsOpenProperty, false);
        Assert.IsFalse(provider.GetRequiredService<MainViewModel>().IsDeviceManagerFlyoutOpen);
        toggleButton.Command.Execute(null);
        Assert.AreEqual(248d, sidebarColumn.Width.Value);
    }

    private static void VerifyRandomDeviceInfoUpdateButton(IServiceProvider provider)
    {
        RandomDeviceInfoDialog dialog = provider.GetRequiredService<RandomDeviceInfoDialog>();
        var button = Assert.IsInstanceOfType<Button>(dialog.FindName("UpdateRandomDeviceInfoButton"));
        Assert.IsInstanceOfType<System.Windows.Controls.Grid>(button.Content);
        var icon = Assert.IsInstanceOfType<MaterialDesignThemes.Wpf.PackIcon>(
            dialog.FindName("UpdateRandomDeviceInfoIcon"));
        var text = Assert.IsInstanceOfType<TextBlock>(dialog.FindName("UpdateRandomDeviceInfoText"));
        object accentForeground = Application.Current.FindResource("Brush.AccentForeground");

        Style materialPrimary = Assert.IsInstanceOfType<Style>(
            Application.Current.FindResource("MaterialPrimaryButtonStyle"));
        Assert.AreSame(materialPrimary, button.Style.BasedOn);
        Assert.AreSame(Application.Current.FindResource("MaterialDesignRaisedButton"), materialPrimary.BasedOn);
        Assert.AreEqual(MaterialDesignThemes.Wpf.PackIconKind.PlayArrow, icon.Kind);
        Assert.AreSame(accentForeground, button.Foreground);
        Assert.AreSame(accentForeground, icon.Foreground);
        Assert.AreSame(accentForeground, text.Foreground);
    }

    private static void VerifyAdvancedChangeConfigDialog(IServiceProvider provider)
    {
        AdvancedChangeConfigDialog dialog = provider.GetRequiredService<AdvancedChangeConfigDialog>();
        AdvancedChangeConfigViewModel viewModel = provider.GetRequiredService<AdvancedChangeConfigViewModel>();
        viewModel.Initialize("SERIAL", new DeviceChangeOptions());
        dialog.DataContext = viewModel;
        MeasureSurface(dialog);

        Assert.AreEqual(980d, dialog.Width);
        Assert.AreEqual(780d, dialog.Height);
        Assert.AreEqual(860d, dialog.MinWidth);
        Assert.AreEqual(680d, dialog.MinHeight);

        var changeAndroidId = Assert.IsInstanceOfType<CheckBox>(dialog.FindName("ChangeAndroidIdCheckBox"));
        var changeMac = Assert.IsInstanceOfType<CheckBox>(dialog.FindName("ChangeMacAddressCheckBox"));
        var useIntegritySecurityPatch = Assert.IsInstanceOfType<CheckBox>(
            dialog.FindName("UseIntegritySecurityPatchCheckBox"));
        var useDeepPackageWipe = Assert.IsInstanceOfType<CheckBox>(
            dialog.FindName("UseDeepPackageWipeCheckBox"));
        var clearAllPackages = Assert.IsInstanceOfType<CheckBox>(dialog.FindName("ClearAllPackagesCheckBox"));
        var clearGoogleAccounts = Assert.IsInstanceOfType<CheckBox>(dialog.FindName("ClearGoogleAccountsCheckBox"));
        var clearGooglePackages = Assert.IsInstanceOfType<CheckBox>(dialog.FindName("ClearGooglePackagesCheckBox"));
        var clearSelectedPackages = Assert.IsInstanceOfType<CheckBox>(dialog.FindName("ClearSelectedPackagesCheckBox"));
        var packagePanel = Assert.IsInstanceOfType<Border>(dialog.FindName("PackageSelectionPanel"));
        var packageScope = Assert.IsInstanceOfType<ComboBox>(dialog.FindName("PackageScopeComboBox"));
        var loadPackagesButton = Assert.IsInstanceOfType<Button>(dialog.FindName("LoadPackagesButton"));
        var loadPackagesText = Assert.IsInstanceOfType<TextBlock>(dialog.FindName("LoadPackagesButtonText"));
        Button[] transferButtons =
        [
            Assert.IsInstanceOfType<Button>(dialog.FindName("AddSelectedPackageButton")),
            Assert.IsInstanceOfType<Button>(dialog.FindName("RemoveSelectedPackageButton")),
            Assert.IsInstanceOfType<Button>(dialog.FindName("AddAllPackagesButton")),
            Assert.IsInstanceOfType<Button>(dialog.FindName("RemoveAllPackagesButton"))
        ];
        var saveButton = Assert.IsInstanceOfType<Button>(dialog.FindName("SaveAdvancedConfigButton"));
        var saveButtonText = Assert.IsInstanceOfType<TextBlock>(dialog.FindName("SaveAdvancedConfigButtonText"));
        object accentForeground = Application.Current.FindResource("Brush.AccentForeground");

        Assert.IsFalse(changeAndroidId.IsChecked);
        Assert.IsTrue(changeMac.IsChecked);
        Assert.IsTrue(useIntegritySecurityPatch.IsChecked);
        Assert.IsFalse(useDeepPackageWipe.IsChecked);
        Assert.IsTrue(clearAllPackages.IsChecked);
        Assert.IsTrue(clearGoogleAccounts.IsChecked);
        Assert.IsFalse(clearGooglePackages.IsChecked);
        Assert.IsTrue(clearAllPackages.IsEnabled);
        Assert.IsTrue(clearGoogleAccounts.IsEnabled);
        Assert.IsFalse(clearGooglePackages.IsEnabled);
        Assert.IsFalse(clearSelectedPackages.IsEnabled);
        Assert.AreEqual(Visibility.Visible, packagePanel.Visibility);
        Assert.IsFalse(packagePanel.IsEnabled);
        Assert.AreEqual(0.48d, packagePanel.Opacity);
        Assert.HasCount(2, packageScope.Items);
        Assert.AreSame(accentForeground, loadPackagesButton.Foreground);
        Assert.AreSame(accentForeground, loadPackagesText.Foreground);
        foreach (Button transferButton in transferButtons)
        {
            Assert.AreSame(
                dialog.FindResource("AdvancedChangeConfigTransferButtonStyle"),
                transferButton.Style);
            var scale = Assert.IsInstanceOfType<System.Windows.Media.ScaleTransform>(transferButton.LayoutTransform);
            Assert.AreEqual(0.7d, scale.ScaleX);
            Assert.AreEqual(0.7d, scale.ScaleY);
            Assert.AreEqual(new Thickness(0d, 4d, 0d, 4d), transferButton.Margin);
        }
        Assert.AreSame(accentForeground, saveButton.Foreground);
        Assert.AreSame(accentForeground, saveButtonText.Foreground);

        viewModel.ClearAllPackages = false;
        dialog.UpdateLayout();
        Assert.IsTrue(clearGooglePackages.IsEnabled);
        Assert.IsTrue(clearSelectedPackages.IsEnabled);

        viewModel.ClearSelectedPackages = true;
        dialog.UpdateLayout();
        Assert.AreEqual(Visibility.Visible, packagePanel.Visibility);
        Assert.IsTrue(packagePanel.IsEnabled);

        viewModel.ClearAllPackages = true;
        dialog.UpdateLayout();
        Assert.AreEqual(Visibility.Visible, packagePanel.Visibility);
        Assert.IsFalse(packagePanel.IsEnabled);
        Assert.IsTrue(clearAllPackages.IsEnabled);
        Assert.IsTrue(clearGoogleAccounts.IsEnabled);
        Assert.IsFalse(clearGooglePackages.IsEnabled);
        Assert.IsFalse(clearSelectedPackages.IsEnabled);
        Assert.AreEqual(0.48d, packagePanel.Opacity);
    }

    private static void VerifyConfirmationDialog(IServiceProvider provider)
    {
        ConfirmationDialog dialog = provider.GetRequiredService<ConfirmationDialog>();
        ConfirmationDialogViewModel viewModel = provider.GetRequiredService<ConfirmationDialogViewModel>();
        viewModel.Initialize("Phone - SERIAL", "Confirmation message", "Warning message", "Yes", "No");
        dialog.DataContext = viewModel;
        MeasureSurface(dialog);

        var icon = Assert.IsInstanceOfType<MaterialDesignThemes.Wpf.PackIcon>(
            dialog.FindName("WarningIcon"));
        var actionIcon = Assert.IsInstanceOfType<MaterialDesignThemes.Wpf.PackIcon>(
            dialog.FindName("ActionIcon"));
        var closeButton = Assert.IsInstanceOfType<Button>(dialog.FindName("CloseButton"));
        var closeIcon = Assert.IsInstanceOfType<MaterialDesignThemes.Wpf.PackIcon>(closeButton.Content);
        var message = Assert.IsInstanceOfType<TextBlock>(dialog.FindName("ConfirmationMessage"));
        var noButton = Assert.IsInstanceOfType<Button>(dialog.FindName("NoButton"));
        var yesButton = Assert.IsInstanceOfType<Button>(dialog.FindName("YesButton"));
        var yesButtonText = Assert.IsInstanceOfType<TextBlock>(dialog.FindName("YesButtonText"));
        object accentForeground = Application.Current.FindResource("Brush.AccentForeground");

        Assert.AreEqual(560d, dialog.Width);
        Assert.AreEqual(SizeToContent.Height, dialog.SizeToContent);
        Assert.AreEqual(MaterialDesignThemes.Wpf.PackIconKind.Alert, icon.Kind);
        Assert.AreEqual(MaterialDesignThemes.Wpf.PackIconKind.HelpCircleOutline, actionIcon.Kind);
        Assert.AreSame(Application.Current.FindResource("Brush.Danger"), closeButton.Background);
        Assert.AreSame(Application.Current.FindResource("Brush.AccentForeground"), closeButton.Foreground);
        Assert.AreSame(Application.Current.FindResource("Brush.AccentForeground"), closeIcon.Foreground);
        Assert.AreSame(Application.Current.FindResource("Brush.Warning"), icon.Foreground);
        Assert.AreEqual(TextWrapping.Wrap, message.TextWrapping);
        Assert.IsTrue(noButton.IsCancel);
        Assert.IsTrue(noButton.IsDefault);
        Assert.AreSame(Application.Current.FindResource(typeof(Button)), noButton.Style);
        Assert.AreSame(Application.Current.FindResource("PrimaryButtonStyle"), yesButton.Style);
        Assert.AreSame(accentForeground, yesButton.Foreground);
        Assert.AreSame(accentForeground, yesButtonText.Foreground);
        Assert.IsFalse(string.IsNullOrWhiteSpace(noButton.Content?.ToString()));
        Assert.IsFalse(string.IsNullOrWhiteSpace(yesButtonText.Text));
    }

    private static void VerifyInteractiveStyles(IServiceProvider provider)
    {
        MainWindow mainWindow = provider.GetRequiredService<MainWindow>();
        DeviceManagerView deviceManagerView = provider.GetRequiredService<DeviceManagerView>();

        AssertButtonStyleStates(mainWindow, "SidebarTabStyle");
        AssertButtonStyleStates(mainWindow, "SidebarDeviceManagerGroupStyle");
        AssertButtonStyleStates(mainWindow, "SidebarSubmenuButtonStyle");
        AssertButtonStyleStates(mainWindow, "BottomIconButtonStyle");
        AssertMenuItemStyleStates(mainWindow, "SidebarFlyoutMenuItemStyle");

        var rowStyle = Assert.IsInstanceOfType<Style>(deviceManagerView.FindResource("DeviceGridRowContextMenuStyle"));
        Assert.IsNotNull(rowStyle.BasedOn, "Device Manager rows must preserve shared hover, selected, and disabled states.");
        Assert.IsTrue(
            rowStyle.Setters.OfType<Setter>().Any(setter => setter.Property == Control.FocusVisualStyleProperty),
            "Device Manager rows must preserve a visible keyboard focus cue.");

        var cellStyle = Assert.IsInstanceOfType<Style>(deviceManagerView.FindResource("DeviceManagerCellStretchStyle"));
        Assert.IsNotNull(cellStyle.BasedOn, "Editable Device Manager cells must preserve shared selection and focus states.");

        var sharedTextEditor = Assert.IsInstanceOfType<Style>(Application.Current.FindResource("InlineDataGridTextBoxStyle"));
        var sharedComboEditor = Assert.IsInstanceOfType<Style>(Application.Current.FindResource("InlineDataGridComboBoxStyle"));
        Assert.IsNotNull(sharedComboEditor.BasedOn);
        AssertStyleTemplateTriggers(
            sharedTextEditor,
            UIElement.IsFocusedProperty,
            UIElement.IsEnabledProperty,
            Validation.HasErrorProperty);

        var gridCheckBoxStyle = Assert.IsInstanceOfType<Style>(Application.Current.FindResource("DataGridCheckBoxStyle"));
        Assert.IsTrue(
            gridCheckBoxStyle.Setters.OfType<Setter>().Any(setter => setter.Property == Control.FocusVisualStyleProperty),
            "Device Manager row checkboxes must define a keyboard focus cue.");

        VerifyColumnRatioApplication();
    }

    private static void VerifyColumnRatioApplication()
    {
        var dataGrid = new DataGrid();
        var nameColumn = new DataGridTextColumn { Width = new DataGridLength(1, DataGridLengthUnitType.Star) };
        var processColumn = new DataGridTextColumn { Width = new DataGridLength(1, DataGridLengthUnitType.Star) };
        DeviceTableColumnLayoutBehavior.SetColumnKey(nameColumn, "Name");
        DeviceTableColumnLayoutBehavior.SetColumnKey(processColumn, "Process");
        dataGrid.Columns.Add(nameColumn);
        dataGrid.Columns.Add(processColumn);
        DeviceTableColumnLayoutBehavior.SetColumnRatios(
            dataGrid,
            new Dictionary<string, double>
            {
                ["Name"] = 0.25,
                ["Process"] = 0.75
            });
        DeviceTableColumnLayoutBehavior.SetPersistColumnRatios(dataGrid, true);

        dataGrid.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent, dataGrid));

        Assert.AreEqual(DataGridLengthUnitType.Star, nameColumn.Width.UnitType);
        Assert.AreEqual(0.25, nameColumn.Width.Value);
        Assert.AreEqual(DataGridLengthUnitType.Star, processColumn.Width.UnitType);
        Assert.AreEqual(0.75, processColumn.Width.Value);
        DeviceTableColumnLayoutBehavior.SetPersistColumnRatios(dataGrid, false);
    }

    private static void AssertButtonStyleStates(FrameworkElement owner, string resourceKey)
    {
        var style = Assert.IsInstanceOfType<Style>(owner.FindResource(resourceKey));
        Assert.IsTrue(
            style.Setters.OfType<Setter>().Any(setter => setter.Property == Control.FocusVisualStyleProperty),
            $"{resourceKey} must define a keyboard focus cue.");
        var template = Assert.IsInstanceOfType<ControlTemplate>(
            style.Setters.OfType<Setter>().Single(setter => setter.Property == Control.TemplateProperty).Value);
        DependencyProperty[] triggerProperties = template.Triggers
            .OfType<Trigger>()
            .Select(trigger => trigger.Property)
            .ToArray();
        Assert.IsTrue(triggerProperties.Contains(UIElement.IsMouseOverProperty), $"{resourceKey} is missing hover state.");
        Assert.IsTrue(triggerProperties.Contains(ButtonBase.IsPressedProperty), $"{resourceKey} is missing pressed state.");
        Assert.IsTrue(triggerProperties.Contains(UIElement.IsEnabledProperty), $"{resourceKey} is missing disabled state.");
    }

    private static void AssertStyleTemplateTriggers(Style style, params DependencyProperty[] expectedProperties)
    {
        var template = Assert.IsInstanceOfType<ControlTemplate>(
            style.Setters.OfType<Setter>().Single(setter => setter.Property == Control.TemplateProperty).Value);
        DependencyProperty[] triggerProperties = template.Triggers
            .OfType<Trigger>()
            .Select(trigger => trigger.Property)
            .ToArray();
        foreach (DependencyProperty expectedProperty in expectedProperties)
        {
            Assert.IsTrue(
                triggerProperties.Contains(expectedProperty),
                $"{style.TargetType.Name} style is missing the {expectedProperty.Name} state.");
        }
    }

    private static void AssertMenuItemStyleStates(FrameworkElement owner, string resourceKey)
    {
        var style = Assert.IsInstanceOfType<Style>(owner.FindResource(resourceKey));
        Assert.IsTrue(
            style.Setters.OfType<Setter>().Any(setter => setter.Property == Control.FocusVisualStyleProperty),
            $"{resourceKey} must define a keyboard focus cue.");
        var template = Assert.IsInstanceOfType<ControlTemplate>(
            style.Setters.OfType<Setter>().Single(setter => setter.Property == Control.TemplateProperty).Value);
        DependencyProperty[] triggerProperties = template.Triggers
            .OfType<Trigger>()
            .Select(trigger => trigger.Property)
            .ToArray();
        RoutedEvent[] eventTriggers = template.Triggers
            .OfType<EventTrigger>()
            .Select(trigger => trigger.RoutedEvent)
            .ToArray();

        Assert.IsTrue(triggerProperties.Contains(MenuItem.IsHighlightedProperty), $"{resourceKey} is missing hover state.");
        Assert.IsFalse(
            triggerProperties.Contains(UIElement.IsKeyboardFocusedProperty),
            $"{resourceKey} must not draw a persistent focus border for mouse interaction.");
        Assert.IsTrue(triggerProperties.Contains(UIElement.IsEnabledProperty), $"{resourceKey} is missing disabled state.");
        Assert.IsTrue(eventTriggers.Contains(UIElement.PreviewMouseDownEvent), $"{resourceKey} is missing pressed state.");
        Assert.IsTrue(eventTriggers.Contains(UIElement.PreviewMouseUpEvent), $"{resourceKey} is missing released state.");
    }

    private static void VerifyEditorRowsAndDataTemplates(IServiceProvider provider)
    {
        DeviceManagerView deviceManagerView = provider.GetRequiredService<DeviceManagerView>();
        var deviceGrid = Assert.IsInstanceOfType<DataGrid>(deviceManagerView.FindName("DeviceGrid"));
        Assert.AreEqual(48d, deviceGrid.RowHeight);
        Assert.AreEqual(220d, deviceGrid.MinHeight);
        Assert.AreEqual(412d, deviceGrid.MaxHeight);
        Assert.IsTrue(deviceGrid.CanUserResizeColumns);
        Assert.AreEqual(ScrollBarVisibility.Auto, deviceGrid.HorizontalScrollBarVisibility);
        Assert.AreEqual(ScrollBarVisibility.Auto, deviceGrid.VerticalScrollBarVisibility);
        string[] expectedColumnKeys =
            ["Index", "Selected", "Serial", "Name", "Type", "Active", "Status", "Process"];

        Assert.HasCount(expectedColumnKeys.Length, deviceGrid.Columns);
        string[] columnKeys = deviceGrid.Columns
            .Select(DeviceTableColumnLayoutBehavior.GetColumnKey)
            .ToArray();
        CollectionAssert.AreEquivalent(expectedColumnKeys, columnKeys);
        Assert.AreEqual(columnKeys.Length, columnKeys.Distinct(StringComparer.Ordinal).Count());
        var deviceManagerRootGrid = Assert.IsInstanceOfType<System.Windows.Controls.Grid>(deviceManagerView.FindName("DeviceManagerRootGrid"));
        Assert.AreEqual(new GridLength(312d), deviceManagerRootGrid.RowDefinitions[2].Height);
        Assert.AreEqual(312d, deviceManagerRootGrid.RowDefinitions[2].MinHeight);
        var deviceProfilePanelScrollViewer = Assert.IsInstanceOfType<ScrollViewer>(deviceManagerView.FindName("DeviceProfilePanelScrollViewer"));
        Assert.AreEqual(ScrollBarVisibility.Auto, deviceProfilePanelScrollViewer.VerticalScrollBarVisibility);
        Assert.AreEqual(ScrollBarVisibility.Disabled, deviceProfilePanelScrollViewer.HorizontalScrollBarVisibility);
        Assert.AreSame(deviceManagerRootGrid, deviceProfilePanelScrollViewer.Parent);
        var deviceProfilePanelContentGrid = Assert.IsInstanceOfType<System.Windows.Controls.Grid>(deviceManagerView.FindName("DeviceProfilePanelContentGrid"));
        Assert.AreEqual(new Thickness(0d, 0d, 16d, 0d), deviceProfilePanelContentGrid.Margin);
        System.Windows.Data.Binding? interactionBinding = System.Windows.Data.BindingOperations.GetBinding(
            deviceProfilePanelContentGrid,
            UIElement.IsEnabledProperty);
        Assert.IsNotNull(interactionBinding);
        Assert.AreEqual(nameof(DeviceManagerViewModel.CanInteractWithSelectedDevice), interactionBinding.Path.Path);
        var deviceActionGrid = Assert.IsInstanceOfType<System.Windows.Controls.Grid>(deviceManagerView.FindName("DeviceActionGrid"));
        Assert.IsFalse(deviceActionGrid.Parent is ScrollViewer);
        Assert.HasCount(7, deviceActionGrid.RowDefinitions);
        AssertGridPosition(deviceManagerView, "RandomDeviceButton", 0, 0);
        AssertGridPosition(deviceManagerView, "WipeWithoutChangeButton", 1, 0);
        AssertGridPosition(deviceManagerView, "RandomSimButton", 2, 0);
        AssertGridPosition(deviceManagerView, "ChangeDeviceButton", 0, 1);
        AssertGridPosition(deviceManagerView, "ChangeWithoutWipeButton", 1, 1);
        AssertGridPosition(deviceManagerView, "ChangeSimButton", 2, 1);
        var viewAllDeviceInfoButton = Assert.IsInstanceOfType<Button>(deviceManagerView.FindName("ViewAllDeviceInfoButton"));
        Assert.IsFalse(viewAllDeviceInfoButton.IsEnabled);
        Assert.AreSame(deviceManagerView.FindResource("DeviceActionButtonStyle"), viewAllDeviceInfoButton.Style.BasedOn);
        AssertGridPosition(deviceManagerView, "DeviceInfoNameTextBox", 0, 1);
        AssertGridPosition(deviceManagerView, "DeviceInfoImeiTextBox", 0, 3);
        AssertGridPosition(deviceManagerView, "DeviceInfoHardwareTextBox", 1, 1);
        AssertGridPosition(deviceManagerView, "DeviceInfoOperatorTextBox", 1, 3);
        AssertGridPosition(deviceManagerView, "DeviceInfoFingerprintTextBox", 2, 1);
        AssertGridPosition(deviceManagerView, "DeviceInfoPhoneNumberTextBox", 2, 3);
        AssertGridPosition(deviceManagerView, "DeviceInfoAndroidVersionTextBox", 3, 1);
        AssertGridPosition(deviceManagerView, "DeviceInfoIccidTextBox", 3, 3);
        AssertGridPosition(deviceManagerView, "DeviceInfoBrandTextBox", 4, 1);
        AssertGridPosition(deviceManagerView, "DeviceInfoImsiTextBox", 4, 3);
        AssertGridPosition(deviceManagerView, "DeviceInfoSerialTextBox", 5, 1);
        AssertGridPosition(deviceManagerView, "DeviceInfoMacTextBox", 5, 3);
        var deviceInfoNameTextBox = Assert.IsInstanceOfType<TextBox>(
            deviceManagerView.FindName("DeviceInfoNameTextBox"));
        var deviceInfoFingerprintTextBox = Assert.IsInstanceOfType<TextBox>(
            deviceManagerView.FindName("DeviceInfoFingerprintTextBox"));
        var deviceInfoFormGrid = Assert.IsInstanceOfType<System.Windows.Controls.Grid>(
            deviceInfoNameTextBox.Parent);
        Assert.HasCount(6, deviceInfoFormGrid.RowDefinitions);
        Assert.AreSame(deviceInfoNameTextBox.Style, deviceInfoFingerprintTextBox.Style);
        Assert.AreEqual(TextWrapping.NoWrap, deviceInfoFingerprintTextBox.TextWrapping);
        Assert.IsFalse(deviceInfoFingerprintTextBox.AcceptsReturn);
        Assert.IsTrue(double.IsNaN(deviceInfoFingerprintTextBox.Width));
        var deviceConfigGrid = Assert.IsInstanceOfType<System.Windows.Controls.Grid>(deviceManagerView.FindName("DeviceConfigFormGrid"));
        Assert.HasCount(7, deviceConfigGrid.RowDefinitions);
        var defaultChangeModeCheckBox = Assert.IsInstanceOfType<CheckBox>(
            deviceManagerView.FindName("UseDefaultChangeModeCheckBox"));
        Assert.AreEqual(6, System.Windows.Controls.Grid.GetRow(defaultChangeModeCheckBox));
        Assert.IsTrue(defaultChangeModeCheckBox.IsChecked);
        var advancedChangeConfigButton = Assert.IsInstanceOfType<Button>(
            deviceManagerView.FindName("AdvancedChangeConfigButton"));
        var deviceConfigHeaderGrid = Assert.IsInstanceOfType<System.Windows.Controls.Grid>(
            deviceManagerView.FindName("DeviceConfigHeaderGrid"));
        Assert.AreSame(deviceConfigHeaderGrid, advancedChangeConfigButton.Parent);
        Assert.AreEqual(2, System.Windows.Controls.Grid.GetColumn(advancedChangeConfigButton));
        Assert.IsFalse(advancedChangeConfigButton.IsEnabled);
        Assert.AreSame(
            deviceManagerView.FindResource("DeviceActionButtonStyle"),
            advancedChangeConfigButton.Style.BasedOn);
        Assert.AreEqual(new Thickness(8d, 0d, 0d, 0d), advancedChangeConfigButton.Margin);
        Assert.IsInstanceOfType<System.Windows.Controls.Grid>(advancedChangeConfigButton.Content);
        Assert.IsFalse(deviceConfigGrid.Children
            .OfType<System.Windows.Controls.TextBlock>()
            .Any(textBlock => string.Equals(textBlock.Text, "Timezone", StringComparison.OrdinalIgnoreCase)
                || string.Equals(textBlock.Text, "Múi giờ", StringComparison.OrdinalIgnoreCase)));

        AddDevicesDialog addDevicesDialog = provider.GetRequiredService<AddDevicesDialog>();
        var addDevicesGrid = Assert.IsInstanceOfType<System.Windows.Controls.DataGrid>(addDevicesDialog.FindName("AddDevicesGrid"));
        Assert.AreEqual(48d, addDevicesGrid.RowHeight);

        var nameTextBox = new System.Windows.Controls.TextBox
        {
            Style = Assert.IsInstanceOfType<Style>(Application.Current.FindResource("InlineDataGridTextBoxStyle"))
        };
        var typeComboBox = new System.Windows.Controls.ComboBox
        {
            Style = Assert.IsInstanceOfType<Style>(Application.Current.FindResource("InlineDataGridComboBoxStyle"))
        };
        Assert.AreEqual(typeComboBox.Height, nameTextBox.Height);
        Assert.AreEqual(40d, nameTextBox.Height);

        AssertTemplateText(deviceManagerView, "BrandComboBox", "Samsung", "Samsung");
        AssertTemplateText(deviceManagerView, "AndroidVersionComboBox", "Android 15", "Android 15");
        AssertTemplateText(
            deviceManagerView,
            "CountryComboBox",
            new CarrierCountryOption("vn", "84", "Vietnam"),
            "Vietnam (VN)");
        AssertTemplateText(
            deviceManagerView,
            "CarrierComboBox",
            new CarrierOption("Viettel", "452", "04"),
            "Viettel (MCC 452 / MNC 04)");

        RandomDeviceInfoDialog randomDeviceInfoDialog = provider.GetRequiredService<RandomDeviceInfoDialog>();
        var randomDeviceInfoFields = Assert.IsInstanceOfType<ItemsControl>(randomDeviceInfoDialog.FindName("RandomDeviceInfoFields"));
        var randomDeviceInfoPanel = Assert.IsInstanceOfType<UniformGrid>(randomDeviceInfoFields.ItemsPanel.LoadContent());
        Assert.AreEqual(2, randomDeviceInfoPanel.Columns);
        Assert.IsInstanceOfType<Button>(randomDeviceInfoDialog.FindName("UpdateRandomDeviceInfoButton"));
        Style randomDeviceInfoInputStyle = Assert.IsInstanceOfType<Style>(
            randomDeviceInfoDialog.FindResource("RandomDeviceInfoInputStyle"));
        foreach (string fieldKey in new[] { "Fingerprint", "Serial" })
        {
            var input = new TextBox
            {
                DataContext = new RandomDeviceInfoField(fieldKey, fieldKey, $"long/{fieldKey}/value"),
                Style = randomDeviceInfoInputStyle
            };
            input.Measure(new Size(300, double.PositiveInfinity));
            input.ApplyTemplate();
            var contentHost = Assert.IsInstanceOfType<ScrollViewer>(
                input.Template.FindName("PART_ContentHost", input));

            Assert.IsTrue(double.IsNaN(input.Height));
            Assert.AreEqual(TextWrapping.Wrap, input.TextWrapping);
            Assert.AreEqual(ScrollBarVisibility.Disabled, input.HorizontalScrollBarVisibility);
            Assert.AreEqual(ScrollBarVisibility.Disabled, input.VerticalScrollBarVisibility);
            Assert.AreEqual(ScrollBarVisibility.Disabled, contentHost.HorizontalScrollBarVisibility);
            Assert.AreEqual(ScrollBarVisibility.Disabled, contentHost.VerticalScrollBarVisibility);
        }

        ChangeTimezoneDialog timezoneDialog = provider.GetRequiredService<ChangeTimezoneDialog>();
        AssertTemplateText(
            timezoneDialog,
            "CountryComboBox",
            new CountryOption("VN", "Vietnam"),
            "Vietnam (VN)");
        AssertTemplateText(
            timezoneDialog,
            "TimezoneComboBox",
            new TimezoneOption("VN", "Vietnam", "Asia/Ho_Chi_Minh", "UTC +07:00"),
            "Asia/Ho_Chi_Minh (UTC +07:00)");
    }

    private static void AssertGridPosition(
        FrameworkElement owner,
        string elementName,
        int expectedRow,
        int expectedColumn)
    {
        var element = Assert.IsInstanceOfType<FrameworkElement>(owner.FindName(elementName));
        Assert.AreEqual(expectedRow, System.Windows.Controls.Grid.GetRow(element), elementName);
        Assert.AreEqual(expectedColumn, System.Windows.Controls.Grid.GetColumn(element), elementName);
    }

    private static void AssertTemplateText(
        FrameworkElement owner,
        string comboBoxName,
        object item,
        string expectedText)
    {
        var comboBox = Assert.IsInstanceOfType<System.Windows.Controls.ComboBox>(owner.FindName(comboBoxName));
        Assert.IsNotNull(comboBox.ItemTemplate, $"{comboBoxName} must use an explicit data template.");
        var textBlock = Assert.IsInstanceOfType<System.Windows.Controls.TextBlock>(comboBox.ItemTemplate.LoadContent());
        textBlock.DataContext = item;
        textBlock.Measure(new Size(600, 100));
        textBlock.Arrange(new Rect(new Point(), textBlock.DesiredSize));
        textBlock.UpdateLayout();
        Assert.AreEqual(expectedText, textBlock.Text, comboBoxName);
    }

    private static void MeasureSurface(FrameworkElement surface)
    {
        FrameworkElement layoutRoot = surface is Window window && window.Content is FrameworkElement content
            ? content
            : surface;
        var sizes = new[]
        {
            new Size(Math.Max(surface.MinWidth, 320), Math.Max(surface.MinHeight, 240)),
            new Size(
                GetLayoutDimension(surface.Width, surface.MinWidth, 980),
                GetLayoutDimension(surface.Height, surface.MinHeight, 640)),
            new Size(Math.Max(surface.MinWidth, 1280), Math.Max(surface.MinHeight, 800)),
        };

        foreach (Size size in sizes)
        {
            try
            {
                layoutRoot.Measure(size);
            }
            catch (InvalidOperationException exception)
            {
                string invalidMargins = string.Join(
                    Environment.NewLine,
                    FindElementsWithInvalidMargin(layoutRoot));
                throw new InvalidOperationException(
                    $"Failed to measure {surface.GetType().Name}.{Environment.NewLine}{invalidMargins}",
                    exception);
            }

            layoutRoot.Arrange(new Rect(new Point(), size));
            layoutRoot.UpdateLayout();

            Assert.IsFalse(double.IsNaN(layoutRoot.DesiredSize.Width));
            Assert.IsFalse(double.IsNaN(layoutRoot.DesiredSize.Height));
            Assert.IsGreaterThanOrEqualTo(0, layoutRoot.ActualWidth);
            Assert.IsGreaterThanOrEqualTo(0, layoutRoot.ActualHeight);
        }
    }

    private static IEnumerable<string> FindElementsWithInvalidMargin(DependencyObject root)
    {
        var pending = new Queue<DependencyObject>();
        pending.Enqueue(root);

        while (pending.Count > 0)
        {
            DependencyObject current = pending.Dequeue();
            if (current is FrameworkElement element)
            {
                string? invalidMargin = null;
                try
                {
                    _ = element.Margin;
                }
                catch (InvalidOperationException exception)
                {
                    invalidMargin = $"{element.GetType().Name} '{element.Name}': {exception.Message}";
                }

                if (invalidMargin != null)
                    yield return invalidMargin;
            }

            int childCount = System.Windows.Media.VisualTreeHelper.GetChildrenCount(current);
            for (int index = 0; index < childCount; index++)
                pending.Enqueue(System.Windows.Media.VisualTreeHelper.GetChild(current, index));
        }
    }

    private static double GetLayoutDimension(double requested, double minimum, double fallback)
    {
        double value = double.IsNaN(requested) || requested <= 0 ? fallback : requested;
        return Math.Max(value, minimum);
    }

}
