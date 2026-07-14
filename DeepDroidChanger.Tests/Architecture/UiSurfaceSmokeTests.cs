using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using DeepDroidChanger.Behaviors;
using DeepDroidChanger.Constants;
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
                VerifyInteractiveStyles();
                VerifyEditorRowsAndDataTemplates(provider);

                foreach (string language in new[] { LanguageConstants.English, LanguageConstants.Vietnamese })
                {
                    localization.ApplyLanguage(language);
                    foreach (string theme in new[] { ThemeConstants.Light, ThemeConstants.Dark })
                    {
                        themes.ApplyTheme(theme);
                        MeasureSurface(provider.GetRequiredService<MainWindow>());
                        MeasureSurface(provider.GetRequiredService<DeviceManagerView>());
                        MeasureSurface(provider.GetRequiredService<SettingsView>());
                        MeasureDialog<LoginDialog, LoginViewModel>(provider);
                        MeasureDialog<AddDevicesDialog, AddDevicesViewModel>(provider);
                        MeasureDialog<DeleteDeviceConfirmationDialog, DeleteDeviceConfirmationViewModel>(provider);
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

        var settingsButton = Assert.IsInstanceOfType<Button>(mainWindow.FindName("BtnSettings"));
        Assert.IsNotNull(settingsButton.Command);
        settingsButton.Command.Execute(null);
        Assert.AreSame(provider.GetRequiredService<SettingsView>(), mainContent.Content);

        var deviceManagerButton = Assert.IsInstanceOfType<Button>(mainWindow.FindName("BtnDeviceManager"));
        Assert.IsNotNull(deviceManagerButton.Command);
        deviceManagerButton.Command.Execute(null);
        Assert.AreSame(provider.GetRequiredService<DeviceManagerView>(), mainContent.Content);

        var toggleButton = Assert.IsInstanceOfType<Button>(mainWindow.FindName("BtnToggle"));
        var sidebarColumn = Assert.IsInstanceOfType<ColumnDefinition>(mainWindow.FindName("SidebarColumn"));
        Assert.IsNotNull(toggleButton.Command);
        toggleButton.Command.Execute(null);
        Assert.AreEqual(56d, sidebarColumn.Width.Value);
        toggleButton.Command.Execute(null);
        Assert.AreEqual(248d, sidebarColumn.Width.Value);
    }

    private static void VerifyInteractiveStyles()
    {
        AssertButtonStyleStates("SidebarTabStyle");
        AssertButtonStyleStates("BottomIconButtonStyle");

        var rowStyle = Assert.IsInstanceOfType<Style>(Application.Current.FindResource("DeviceGridRowContextMenuStyle"));
        Assert.IsNotNull(rowStyle.BasedOn, "Device Manager rows must preserve shared hover, selected, and disabled states.");
        Assert.IsTrue(
            rowStyle.Setters.OfType<Setter>().Any(setter => setter.Property == Control.FocusVisualStyleProperty),
            "Device Manager rows must preserve a visible keyboard focus cue.");

        var cellStyle = Assert.IsInstanceOfType<Style>(Application.Current.FindResource("DeviceManagerCellStretchStyle"));
        Assert.IsNotNull(cellStyle.BasedOn, "Editable Device Manager cells must preserve shared selection and focus states.");

        var sharedTextEditor = Assert.IsInstanceOfType<Style>(Application.Current.FindResource("InlineDataGridTextBoxStyle"));
        var sharedComboEditor = Assert.IsInstanceOfType<Style>(Application.Current.FindResource("InlineDataGridComboBoxStyle"));
        var addTextEditor = Assert.IsInstanceOfType<Style>(Application.Current.FindResource("AddDeviceNameTextBoxStyle"));
        var addComboEditor = Assert.IsInstanceOfType<Style>(Application.Current.FindResource("AddDeviceTypeComboBoxStyle"));
        Assert.AreSame(sharedTextEditor, addTextEditor.BasedOn);
        Assert.AreSame(sharedComboEditor, addComboEditor.BasedOn);
        AssertStyleTemplateTriggers(
            sharedTextEditor,
            UIElement.IsFocusedProperty,
            UIElement.IsEnabledProperty,
            Validation.HasErrorProperty);

        var gridCheckBoxStyle = Assert.IsInstanceOfType<Style>(Application.Current.FindResource("DeviceGridCheckBoxStyle"));
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
        DeviceTableColumnLayoutBehavior.SetColumnKey(nameColumn, DeviceTableColumnSettings.Name);
        DeviceTableColumnLayoutBehavior.SetColumnKey(processColumn, DeviceTableColumnSettings.Process);
        dataGrid.Columns.Add(nameColumn);
        dataGrid.Columns.Add(processColumn);
        DeviceTableColumnLayoutBehavior.SetColumnRatios(
            dataGrid,
            new Dictionary<string, double>
            {
                [DeviceTableColumnSettings.Name] = 0.25,
                [DeviceTableColumnSettings.Process] = 0.75
            });
        DeviceTableColumnLayoutBehavior.SetPersistColumnRatios(dataGrid, true);

        dataGrid.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent, dataGrid));

        Assert.AreEqual(DataGridLengthUnitType.Star, nameColumn.Width.UnitType);
        Assert.AreEqual(0.25, nameColumn.Width.Value);
        Assert.AreEqual(DataGridLengthUnitType.Star, processColumn.Width.UnitType);
        Assert.AreEqual(0.75, processColumn.Width.Value);
        DeviceTableColumnLayoutBehavior.SetPersistColumnRatios(dataGrid, false);
    }

    private static void AssertButtonStyleStates(string resourceKey)
    {
        var style = Assert.IsInstanceOfType<Style>(Application.Current.FindResource(resourceKey));
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

    private static void VerifyEditorRowsAndDataTemplates(IServiceProvider provider)
    {
        DeviceManagerView deviceManagerView = provider.GetRequiredService<DeviceManagerView>();
        var deviceGrid = Assert.IsInstanceOfType<DataGrid>(deviceManagerView.FindName("DeviceGrid"));
        Assert.AreEqual(48d, deviceGrid.RowHeight);
        Assert.AreEqual(220d, deviceGrid.MinHeight);
        Assert.AreEqual(364d, deviceGrid.MaxHeight);
        Assert.IsTrue(deviceGrid.CanUserResizeColumns);
        Assert.AreEqual(ScrollBarVisibility.Auto, deviceGrid.HorizontalScrollBarVisibility);
        Assert.AreEqual(ScrollBarVisibility.Auto, deviceGrid.VerticalScrollBarVisibility);
        Assert.HasCount(DeviceTableColumnSettings.DefaultRatios.Count, deviceGrid.Columns);
        string[] columnKeys = deviceGrid.Columns
            .Select(DeviceTableColumnLayoutBehavior.GetColumnKey)
            .ToArray();
        CollectionAssert.AreEquivalent(DeviceTableColumnSettings.DefaultRatios.Keys.ToArray(), columnKeys);
        Assert.AreEqual(columnKeys.Length, columnKeys.Distinct(StringComparer.Ordinal).Count());
        var deviceConfigGrid = Assert.IsInstanceOfType<System.Windows.Controls.Grid>(deviceManagerView.FindName("DeviceConfigFormGrid"));
        Assert.HasCount(5, deviceConfigGrid.RowDefinitions);
        Assert.IsFalse(deviceConfigGrid.Children
            .OfType<System.Windows.Controls.TextBlock>()
            .Any(textBlock => string.Equals(textBlock.Text, "Timezone", StringComparison.OrdinalIgnoreCase)
                || string.Equals(textBlock.Text, "Múi giờ", StringComparison.OrdinalIgnoreCase)));

        AddDevicesDialog addDevicesDialog = provider.GetRequiredService<AddDevicesDialog>();
        var addDevicesGrid = Assert.IsInstanceOfType<System.Windows.Controls.DataGrid>(addDevicesDialog.FindName("AddDevicesGrid"));
        Assert.AreEqual(48d, addDevicesGrid.RowHeight);

        var nameTextBox = new System.Windows.Controls.TextBox
        {
            Style = Assert.IsInstanceOfType<Style>(Application.Current.FindResource("AddDeviceNameTextBoxStyle"))
        };
        var typeComboBox = new System.Windows.Controls.ComboBox
        {
            Style = Assert.IsInstanceOfType<Style>(Application.Current.FindResource("AddDeviceTypeComboBoxStyle"))
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

        ChangeTimezoneDialog timezoneDialog = provider.GetRequiredService<ChangeTimezoneDialog>();
        AssertTemplateText(
            timezoneDialog,
            "TimezoneComboBox",
            new TimezoneOption("VN", "Vietnam", "Asia/Ho_Chi_Minh", "UTC +07:00", "Vietnam — Asia/Ho_Chi_Minh (UTC +07:00)"),
            "Vietnam — Asia/Ho_Chi_Minh (UTC +07:00)");
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
            layoutRoot.Measure(size);
            layoutRoot.Arrange(new Rect(new Point(), size));
            layoutRoot.UpdateLayout();

            Assert.IsFalse(double.IsNaN(layoutRoot.DesiredSize.Width));
            Assert.IsFalse(double.IsNaN(layoutRoot.DesiredSize.Height));
            Assert.IsGreaterThanOrEqualTo(0, layoutRoot.ActualWidth);
            Assert.IsGreaterThanOrEqualTo(0, layoutRoot.ActualHeight);
        }
    }

    private static double GetLayoutDimension(double requested, double minimum, double fallback)
    {
        double value = double.IsNaN(requested) || requested <= 0 ? fallback : requested;
        return Math.Max(value, minimum);
    }

}
