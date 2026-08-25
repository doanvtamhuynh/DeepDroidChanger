using System.Windows;
using DeepDroidChanger.Authentication;
using DeepDroidChanger.Helpers;
using DeepDroidChanger.Models;
using DeepDroidChanger.Services;
using DeepDroidChanger.ViewModels;
using DeepDroidChanger.Views;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DeepDroidChanger;

public sealed partial class App : Application
{
    private IHost? _host;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        AppSettings settings = new();
        _host = Host.CreateDefaultBuilder(e.Args)
            .ConfigureLogging(logging => logging.SetMinimumLevel(LogLevel.Debug))
            .ConfigureServices((_, services) => RegisterServices(services, settings))
            .Build();

        try
        {
            await _host.StartAsync().ConfigureAwait(true);

            _host.Services.GetRequiredService<IRuntimeDataMigrationService>().Migrate();

            AppSettings loadedSettings = await _host.Services
                .GetRequiredService<ISettingsService>()
                .LoadAsync(CancellationToken.None)
                .ConfigureAwait(true);
            CopySettings(loadedSettings, settings);

            _host.Services.GetRequiredService<ILocalizationService>().ApplyLanguage(settings.Language);
            _host.Services.GetRequiredService<IThemeService>().ApplyTheme(settings.Theme);

            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            bool authenticated = await _host.Services
                .GetRequiredService<ILoginDialogService>()
                .ShowLoginAsync(CancellationToken.None)
                .ConfigureAwait(true);

            if (!authenticated)
            {
                Shutdown();
                return;
            }

            MainWindow mainWindow = _host.Services.GetRequiredService<MainWindow>();
            MainWindow = mainWindow;
            ShutdownMode = ShutdownMode.OnMainWindowClose;
            mainWindow.Show();
        }
        catch (OperationCanceledException)
        {
            Shutdown();
        }
        catch (Exception exception)
        {
            _host.Services.GetRequiredService<ILogger<App>>()
                .LogError(exception, "Application startup failed.");
            Shutdown();
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_host is not null)
        {
            IHost host = _host;
            ILogger<App>? logger = host.Services.GetService<ILogger<App>>();
            try
            {
                // WPF does not await an async OnExit override. The MainWindow
                // has already waited for device actions (or explicitly forced
                // the exit timeout) at this point, so complete host teardown
                // deterministically before the process terminates.
                host.StopAsync().GetAwaiter().GetResult();
            }
            catch (Exception exception)
            {
                logger?.LogError(exception, "Application host shutdown failed.");
            }
            finally
            {
                try
                {
                    host.Dispose();
                }
                catch (Exception exception)
                {
                    logger?.LogError(exception, "Application host disposal failed.");
                }

                _host = null;
            }
        }

        base.OnExit(e);
    }

    internal static void RegisterServices(IServiceCollection services, AppSettings settings)
    {
        services.AddSingleton(settings);

        services.AddSingleton<IThemeService, ThemeService>();
        services.AddSingleton<ILocalizationService, LocalizationService>();
        services.AddTransient<IFilePickerDialogService, FilePickerDialogService>();
        services.AddSingleton<IRandomService, RandomService>();
        services.AddSingleton<IProcessRunnerService, ProcessRunnerService>();
        services.AddSingleton<AdbToolPathResolver>();
        services.AddSingleton<IUiDispatcherService, UiDispatcherService>();
        services.AddSingleton<IPollingService, PollingService>();
        services.AddSingleton<IFileSystemService, FileSystemService>();

        services.AddSingleton<ISettingsService, SettingsService>();
        services.AddSingleton<IRuntimeDataMigrationService, RuntimeDataMigrationService>();
        services.AddSingleton<IDeviceStoreService, DeviceStoreService>();
        services.AddSingleton<ICarrierDataService, CarrierDataService>();
        services.AddSingleton<ILocationDataService, LocationDataService>();
        services.AddDeepDroidAuthentication();
        services.AddSingleton<IDeviceRandomApiService, DeviceRandomApiService>();
        services.AddSingleton<IIpGeolocationService, IpGeolocationService>();
        services.AddOptions<DeviceInfoApiOptions>()
            .Configure(DeviceInfoApiOptionsHelper.ApplyDefaults)
            .Validate(DeviceInfoApiOptionsHelper.IsValid, "Device Info API configuration is invalid.");

        services.AddSingleton<IAdbCommandService, AdbCommandService>();
        services.AddSingleton<IAdbDeviceService, AdbDeviceService>();
        services.AddSingleton<IAdbDeviceTrackerService, AdbDeviceTrackerService>();
        services.AddSingleton<IDeviceTimezoneService, DeviceTimezoneService>();
        services.AddSingleton<IDeviceLocationService, DeviceLocationService>();
        services.AddSingleton<IProxyService, ProxyService>();
        services.AddSingleton<IDeviceIntegrityService, DeviceIntegrityService>();
        services.AddSingleton<IDevicePackageService, DevicePackageService>();
        services.AddSingleton<IDeviceDataCleanupService, DeviceDataCleanupService>();
        services.AddSingleton<IDeviceChangeService, DeviceChangeService>();
        services.AddSingleton<IXapkPackageService, XapkPackageService>();
        services.AddSingleton<IPackageInstallService, PackageInstallService>();
        services.AddSingleton<IDeviceRandomProfileService, DeviceRandomProfileService>();
        services.AddSingleton<ISimProfileService, SimProfileService>();
        services.AddSingleton<IDeviceListService, DeviceListService>();
        services.AddSingleton<IDeviceConfigService, DeviceConfigService>();
        services.AddSingleton<IMultipleDeviceConfigService, MultipleDeviceConfigService>();
        services.AddSingleton<IRandomDeviceService, RandomDeviceService>();
        services.AddSingleton<IDeviceActionCoordinatorService, DeviceActionCoordinatorService>();
        services.AddSingleton<IDeviceProcessStateService, DeviceProcessStateService>();
        services.AddSingleton<IDeviceActionEligibilityService, DeviceActionEligibilityService>();
        services.AddSingleton<IDeviceActionFeedbackService, DeviceActionFeedbackService>();
        services.AddSingleton<IDeviceActionService, DeviceActionService>();
        services.AddSingleton<IProxyWorkflowService, ProxyWorkflowService>();
        services.AddSingleton<IClipboardService, ClipboardService>();
        services.AddTransient<IAddDevicesDialogService, AddDevicesDialogService>();
        services.AddTransient<ILoginDialogService, LoginDialogService>();
        services.AddTransient<IChangeLocationDialogService, ChangeLocationDialogService>();
        services.AddTransient<IChangeTimezoneDialogService, ChangeTimezoneDialogService>();
        services.AddTransient<IConfirmationDialogService, ConfirmationDialogService>();
        services.AddTransient<IDeviceActionConfirmationDialogService, DeviceActionConfirmationDialogService>();
        services.AddTransient<IAdvancedChangeConfigDialogService, AdvancedChangeConfigDialogService>();
        services.AddTransient<IRandomDeviceInfoDialogService, RandomDeviceInfoDialogService>();
        services.AddTransient<IFakeProxyDialogService, FakeProxyDialogService>();
        services.AddTransient<IFakeProxyBatchDialogService, FakeProxyBatchDialogService>();
        services.AddTransient<IUpdateIntegrityDialogService, UpdateIntegrityDialogService>();
        services.AddTransient<IInstallPackageDialogService, InstallPackageDialogService>();
        services.AddSingleton<MainViewModel>();
        services.AddSingleton<ChangeSingleDeviceViewModel>();
        services.AddSingleton<ChangeMultipleDevicesViewModel>();
        services.AddSingleton<SettingsViewModel>();
        services.AddSingleton<MainWindow>();
        services.AddSingleton<ChangeSingleDeviceView>();
        services.AddSingleton<ChangeMultipleDevicesView>();
        services.AddSingleton<SettingsView>();

        services.AddTransient<LoginViewModel>();
        services.AddTransient<AddDevicesViewModel>();
        services.AddTransient<ChangeLocationViewModel>();
        services.AddTransient<ChangeTimezoneViewModel>();
        services.AddTransient<ConfirmationDialogViewModel>();
        services.AddTransient<AdvancedChangeConfigViewModel>();
        services.AddTransient<RandomDeviceInfoViewModel>();
        services.AddTransient<FakeProxyViewModel>();
        services.AddTransient<FakeProxyBatchViewModel>();
        services.AddTransient<UpdateIntegrityViewModel>();
        services.AddTransient<InstallPackageViewModel>();
        services.AddTransient<InstallPackageBatchViewModel>();

        services.AddTransient<LoginDialog>();
        services.AddTransient<AddDevicesDialog>();
        services.AddTransient<ChangeLocationDialog>();
        services.AddTransient<ChangeTimezoneDialog>();
        services.AddTransient<ConfirmationDialog>();
        services.AddTransient<AdvancedChangeConfigDialog>();
        services.AddTransient<RandomDeviceInfoDialog>();
        services.AddTransient<FakeProxyDialog>();
        services.AddTransient<FakeProxyBatchDialog>();
        services.AddTransient<UpdateIntegrityDialog>();
        services.AddTransient<InstallPackageDialog>();
        services.AddTransient<InstallPackageBatchDialog>();
    }

    private static void CopySettings(AppSettings source, AppSettings target)
    {
        target.Language = source.Language;
        target.Theme = source.Theme;
        target.SidebarCollapsed = source.SidebarCollapsed;
        target.DeviceTableColumnRatios = source.DeviceTableColumnRatios;
        target.SelectedSingleDeviceSerial = source.SelectedSingleDeviceSerial;
        target.SelectedMultipleDeviceSerials = source.SelectedMultipleDeviceSerials;
    }
}
