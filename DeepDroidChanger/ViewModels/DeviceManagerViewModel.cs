using DeepDroidChanger.Services;
using DeepDroidChanger.Models;
using DeepDroidChanger.Constants;
using DeepDroidChanger.Helpers;
using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace DeepDroidChanger.ViewModels
{
    public sealed partial class DeviceManagerViewModel : ObservableObject, IDisposable
    {
        private const int NewDevicePollSeconds = 3;
        private const int DeviceNameSaveDebounceMilliseconds = 300;
        private const string DefaultCountryIso = "us";

        private readonly IAddDevicesDialogService _addDevicesDialogService;
        private readonly ICarrierDataService _carrierDataService;
        private readonly IChangeTimezoneDialogService _changeTimezoneDialogService;
        private readonly IDeviceTimezoneService _deviceTimezoneService;
        private readonly IChangeLocationDialogService _changeLocationDialogService;
        private readonly IDeviceLocationService _deviceLocationService;
        private readonly IFakeProxyDialogService _fakeProxyDialogService;
        private readonly IProxyService _adbProxyService;
        private readonly IProxyWorkflowService _proxyWorkflowService;
        private readonly IUpdateIntegrityDialogService _updateIntegrityDialogService;
        private readonly IDeviceIntegrityService _deviceIntegrityService;
        private readonly IInstallPackageDialogService _installPackageDialogService;
        private readonly IDeviceViewerDialogService _deviceViewerDialogService;
        private readonly IDeleteDeviceConfirmationDialogService _deleteDeviceConfirmationDialogService;
        private readonly IRandomDeviceInfoDialogService _randomDeviceInfoDialogService;
        private readonly IDeviceListService _deviceListService;
        private readonly IDeviceSelectionService _deviceSelectionService;
        private readonly IDeviceConfigService _deviceConfigService;
        private readonly IRandomDeviceService _randomDeviceService;
        private readonly ISimProfileService _simProfileService;
        private readonly IDeviceActionService _deviceActionService;
        private readonly ILocalizationService _localizationService;
        private readonly AppSettings _settings;
        private readonly ILogger<DeviceManagerViewModel> _logger;
        private readonly IUiDispatcherService _uiDispatcher;
        private readonly IPollingService _pollingService;
        private readonly SemaphoreSlim _deviceRefreshLock = new(1, 1);
        private readonly SemaphoreSlim _lifecycleLock = new(1, 1);
        private readonly SemaphoreSlim _randomProfileLock = new(1, 1);
        private readonly object _pendingDeviceEditsLock = new();
        private readonly Dictionary<string, PendingDeviceEdit> _pendingDeviceEdits = new(StringComparer.OrdinalIgnoreCase);
        private readonly object _pendingProfileEditLock = new();
        private readonly Dictionary<string, PendingDeviceProfileEdit> _pendingProfileEdits = new(StringComparer.OrdinalIgnoreCase);
        private CancellationTokenSource? _pollCancellation;
        private Task? _pollTask;
        private List<StoredDeviceConfig> _storedDevices = new();
        private List<CarrierProfile> _carrierProfiles = new();
        private DeviceInfoApiDevice? _lastRandomDeviceInfo;

        private DeviceRowViewModel? _selectedDevice;
        private bool _isRefreshingRows;
        private bool _isSynchronizingSelection;
        private bool _isApplyingDeviceConfig;
        private bool _isUpdatingCarrierOptions;
        [ObservableProperty]
        private bool _isChangeSimEnabled = true;
        [ObservableProperty]
        private bool _useIntegritySecurityPatch;
        private bool _isLoadingDevices;
        private string _newDeviceCountText = string.Empty;
        [ObservableProperty]
        private string? _selectedBrand;
        [ObservableProperty]
        private string? _selectedAndroidVersion;
        [ObservableProperty]
        private CarrierCountryOption? _selectedCountry;
        [ObservableProperty]
        private CarrierOption? _selectedCarrier;
        private bool _isDisposed;
        private bool _isShowingToolbarLog;
        private readonly List<DeviceRowViewModel> _allDeviceRows = new();
        [ObservableProperty]
        private string _selectedDeviceFilter = string.Empty;

        public DeviceManagerViewModel(
            IAddDevicesDialogService addDevicesDialogService,
            ICarrierDataService carrierDataService,
            IChangeTimezoneDialogService changeTimezoneDialogService,
            IDeviceTimezoneService deviceTimezoneService,
            IChangeLocationDialogService changeLocationDialogService,
            IDeviceLocationService deviceLocationService,
            IFakeProxyDialogService fakeProxyDialogService,
            IProxyService adbProxyService,
            IProxyWorkflowService proxyWorkflowService,
            IUpdateIntegrityDialogService updateIntegrityDialogService,
            IDeviceIntegrityService deviceIntegrityService,
            IInstallPackageDialogService installPackageDialogService,
            IDeviceViewerDialogService deviceViewerDialogService,
            IDeleteDeviceConfirmationDialogService deleteDeviceConfirmationDialogService,
            IRandomDeviceInfoDialogService randomDeviceInfoDialogService,
            IDeviceListService deviceListService,
            IDeviceSelectionService deviceSelectionService,
            IDeviceConfigService deviceConfigService,
            IRandomDeviceService randomDeviceService,
            ISimProfileService simProfileService,
            IDeviceActionService deviceActionService,
            ILocalizationService localizationService,
            AppSettings settings,
            IUiDispatcherService uiDispatcher,
            IPollingService pollingService,
            ILogger<DeviceManagerViewModel> logger)
        {
            _addDevicesDialogService = addDevicesDialogService;
            _carrierDataService = carrierDataService;
            _changeTimezoneDialogService = changeTimezoneDialogService;
            _deviceTimezoneService = deviceTimezoneService;
            _changeLocationDialogService = changeLocationDialogService;
            _deviceLocationService = deviceLocationService;
            _fakeProxyDialogService = fakeProxyDialogService;
            _adbProxyService = adbProxyService;
            _proxyWorkflowService = proxyWorkflowService;
            _updateIntegrityDialogService = updateIntegrityDialogService;
            _deviceIntegrityService = deviceIntegrityService;
            _installPackageDialogService = installPackageDialogService;
            _deviceViewerDialogService = deviceViewerDialogService;
            _deleteDeviceConfirmationDialogService = deleteDeviceConfirmationDialogService;
            _randomDeviceInfoDialogService = randomDeviceInfoDialogService;
            _deviceListService = deviceListService;
            _deviceSelectionService = deviceSelectionService;
            _deviceConfigService = deviceConfigService;
            _randomDeviceService = randomDeviceService;
            _simProfileService = simProfileService;
            _deviceActionService = deviceActionService;
            _localizationService = localizationService;
            _settings = settings;
            _uiDispatcher = uiDispatcher;
            _pollingService = pollingService;
            _logger = logger;
            Devices = new ObservableCollection<DeviceRowViewModel>();
            Countries = new ObservableCollection<CarrierCountryOption>();
            Carriers = new ObservableCollection<CarrierOption>();
            AndroidVersions = new ObservableCollection<string>();
            DeviceInfo = CreateDefaultDeviceInfo();

            Brands = DeviceProfileOptions.Brands;
            UpdateAndroidVersionOptions(DeviceProfileOptions.Random, null);
            TypeOptions = DeviceTypeOptions.All;
            NewDeviceCountText = string.Format(_localizationService.GetString("DeviceManager_NewDeviceCount"), 0);

            SelectedBrand = Brands.FirstOrDefault();
            SelectedAndroidVersion = AndroidVersions.FirstOrDefault();
            _selectedDeviceFilter = DeviceFilterKeys.All;
        }

        public ObservableCollection<DeviceRowViewModel> Devices { get; }
        public DeviceInfoFormViewModel DeviceInfo { get; }
        public IReadOnlyList<string> Brands { get; }
        public ObservableCollection<string> AndroidVersions { get; }
        public ObservableCollection<CarrierCountryOption> Countries { get; }
        public ObservableCollection<CarrierOption> Carriers { get; }
        public IReadOnlyList<string> TypeOptions { get; }
        public IReadOnlyDictionary<string, double> DeviceTableColumnRatios => _settings.DeviceTableColumnRatios;

        public async Task InitializeAsync(CancellationToken cancellationToken)
        {
            await _lifecycleLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                ObjectDisposedException.ThrowIf(_isDisposed, this);
                if (_pollTask is { IsCompleted: false })
                    return;

                _pollCancellation?.Dispose();
                _pollCancellation = new CancellationTokenSource();

                await LoadCarrierProfilesAsync(cancellationToken).ConfigureAwait(false);
                await LoadSavedDevicesAsync(cancellationToken).ConfigureAwait(false);
                await RefreshNewDeviceCountAsync(cancellationToken).ConfigureAwait(false);

                cancellationToken.ThrowIfCancellationRequested();
                _pollTask = _pollingService.RunAsync(
                    TimeSpan.FromSeconds(NewDevicePollSeconds),
                    RefreshNewDeviceCountAsync,
                    _pollCancellation.Token);
            }
            catch
            {
                _pollCancellation?.Cancel();
                _pollCancellation?.Dispose();
                _pollCancellation = null;
                _pollTask = null;
                throw;
            }
            finally
            {
                _lifecycleLock.Release();
            }
        }

        public async Task DeactivateAsync()
        {
            await FlushPendingDeviceEditsAsync().ConfigureAwait(false);
            await FlushPendingDeviceProfileAsync().ConfigureAwait(false);
            await _lifecycleLock.WaitAsync().ConfigureAwait(false);
            try
            {
                _pollCancellation?.Cancel();
                if (_pollTask != null)
                    await _pollTask.ConfigureAwait(false);

                _pollTask = null;
                _pollCancellation?.Dispose();
                _pollCancellation = null;
            }
            finally
            {
                _lifecycleLock.Release();
            }
        }

        public DeviceRowViewModel? SelectedDevice
        {
            get => _selectedDevice;
            set
            {
                if (_isSynchronizingSelection)
                {
                    SetProperty(ref _selectedDevice, value);
                    return;
                }

                SelectSingleDevice(value);
            }
        }

        partial void OnSelectedDeviceFilterChanged(string value)
        {
            ApplyDeviceFilter();
        }

        public string NewDeviceCountText
        {
            get => _newDeviceCountText;
            set => SetProperty(ref _newDeviceCountText, value);
        }

        public bool IsLoadingDevices
        {
            get => _isLoadingDevices;
            set
            {
                if (SetProperty(ref _isLoadingDevices, value))
                    AddNewDevicesCommand.NotifyCanExecuteChanged();
            }
        }

        partial void OnSelectedCountryChanged(CarrierCountryOption? value)
        {
            if (_isApplyingDeviceConfig)
                return;

            UpdateCarrierOptionsForCountry(value?.CountryIso, null);
            QueueSelectedDeviceProfileSave();
        }

        partial void OnSelectedCarrierChanged(CarrierOption? value)
        {
            if (_isApplyingDeviceConfig || _isUpdatingCarrierOptions)
                return;

            QueueSelectedDeviceProfileSave();
        }

        partial void OnSelectedBrandChanged(string? value)
        {
            UpdateAndroidVersionOptions(value, SelectedAndroidVersion);

            if (!_isApplyingDeviceConfig)
                QueueSelectedDeviceProfileSave();
        }

        partial void OnSelectedAndroidVersionChanged(string? value)
        {
            if (!_isApplyingDeviceConfig)
                QueueSelectedDeviceProfileSave();
        }

        partial void OnIsChangeSimEnabledChanged(bool value)
        {
            if (!_isApplyingDeviceConfig)
                QueueSelectedDeviceProfileSave();
        }

        partial void OnUseIntegritySecurityPatchChanged(bool value)
        {
            if (!_isApplyingDeviceConfig)
                QueueSelectedDeviceProfileSave();
        }

        public void Dispose()
        {
            if (_isDisposed)
                return;

            FlushPendingDeviceEditsAsync().GetAwaiter().GetResult();
            FlushPendingDeviceProfileAsync().GetAwaiter().GetResult();
            _isDisposed = true;
            _pollCancellation?.Cancel();

            foreach (var device in _allDeviceRows)
                device.PropertyChanged -= OnDeviceRowPropertyChanged;

            _pollCancellation?.Dispose();
            _pollCancellation = null;
        }

        private string GetLogText(string resourceKey)
        {
            return _localizationService.GetString(resourceKey);
        }

        private void SetDeviceLog(DeviceRowViewModel device, string resourceKey, params object[] formatArguments)
        {
            var template = GetLogText(resourceKey);
            var message = formatArguments.Length == 0
                ? template
                : string.Format(template, formatArguments);
            device.Process = message;
            _logger.LogInformation("Device {Serial} action: {Message}", device.Serial, message);
        }

        private async Task ShowDeviceLogAsync(
            DeviceRowViewModel device,
            string resourceKey,
            CancellationToken cancellationToken,
            params object[] formatArguments)
        {
            SetDeviceLog(device, resourceKey, formatArguments);

            try
            {
                await Task.Delay(UiTimingConstants.MinimumActionStatusMilliseconds, cancellationToken).ConfigureAwait(true);
            }
            catch (OperationCanceledException)
            {
            }
        }

        private async Task ShowToolbarLogAsync(string resourceKey, CancellationToken cancellationToken)
        {
            _isShowingToolbarLog = true;
            var message = GetLogText(resourceKey);
            NewDeviceCountText = message;
            _logger.LogInformation("Devices toolbar action: {Message}", message);

            try
            {
                await Task.Delay(UiTimingConstants.MinimumActionStatusMilliseconds, cancellationToken).ConfigureAwait(true);
            }
            catch (OperationCanceledException)
            {
            }
            finally
            {
                _isShowingToolbarLog = false;
                await RefreshNewDeviceCountAsync(CancellationToken.None).ConfigureAwait(true);
            }
        }

        private bool CanAddNewDevices()
        {
            return !IsLoadingDevices;
        }

        [RelayCommand(CanExecute = nameof(CanAddNewDevices))]
        private async Task AddNewDevicesAsync(CancellationToken cancellationToken)
        {
            IsLoadingDevices = true;
            await ShowToolbarLogAsync(DeviceLogResourceKeys.AddDevicesOpening, cancellationToken).ConfigureAwait(true);

            try
            {
                var selectedDevices = await _addDevicesDialogService.ShowAddDevicesAsync(cancellationToken).ConfigureAwait(true);
                if (selectedDevices.Count == 0)
                {
                    await ShowToolbarLogAsync(DeviceLogResourceKeys.AddDevicesCanceled, cancellationToken).ConfigureAwait(true);
                    return;
                }

                await ShowToolbarLogAsync(DeviceLogResourceKeys.SavingDevices, cancellationToken).ConfigureAwait(true);

                await _deviceRefreshLock.WaitAsync(cancellationToken).ConfigureAwait(true);
                try
                {
                    var snapshot = await _deviceListService
                        .AddSelectedDevicesAsync(selectedDevices, cancellationToken)
                        .ConfigureAwait(true);
                    ApplyDeviceListSnapshot(snapshot);
                }
                finally
                {
                    _deviceRefreshLock.Release();
                }

                await ShowToolbarLogAsync(DeviceLogResourceKeys.AddDevicesSuccess, cancellationToken).ConfigureAwait(true);
            }
            catch (OperationCanceledException)
            {
                await ShowToolbarLogAsync(DeviceLogResourceKeys.AddDevicesCanceled, CancellationToken.None).ConfigureAwait(true);
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Failed to add new devices.");
                await ShowToolbarLogAsync(DeviceLogResourceKeys.AddDevicesFailed, CancellationToken.None).ConfigureAwait(true);
            }
            finally
            {
                IsLoadingDevices = false;
            }
        }

        [RelayCommand]
        private async Task DeleteDeviceAsync(DeviceRowViewModel? device, CancellationToken cancellationToken)
        {
            if (device == null)
            {
                await ShowToolbarLogAsync(DeviceLogResourceKeys.SelectDeviceFirst, cancellationToken).ConfigureAwait(true);
                return;
            }

            SelectSingleDevice(device);

            try
            {
                var confirmed = await _deleteDeviceConfirmationDialogService
                    .ShowDeleteDeviceConfirmationAsync(device.Name, device.Serial, cancellationToken)
                    .ConfigureAwait(true);

                if (!confirmed)
                {
                    await ShowDeviceLogAsync(device, DeviceLogResourceKeys.DeleteDeviceCanceled, cancellationToken).ConfigureAwait(true);
                    SetDeviceLog(device, DeviceLogResourceKeys.Ready);
                    return;
                }

                SetDeviceLog(device, DeviceLogResourceKeys.DeletingDevice);

                await _deviceRefreshLock.WaitAsync(cancellationToken).ConfigureAwait(true);
                try
                {
                    var deleteResult = await _deviceListService
                        .DeleteSavedDeviceAsync(device.Serial, cancellationToken)
                        .ConfigureAwait(true);
                    if (!deleteResult.Removed)
                    {
                        await ShowDeviceLogAsync(device, DeviceLogResourceKeys.DeleteDeviceFailed, cancellationToken).ConfigureAwait(true);
                        SetDeviceLog(device, DeviceLogResourceKeys.Ready);
                        return;
                    }

                    ApplyDeviceListSnapshot(deleteResult.Snapshot);
                }
                finally
                {
                    _deviceRefreshLock.Release();
                }

                await ShowToolbarLogAsync(DeviceLogResourceKeys.DeleteDeviceSuccess, cancellationToken).ConfigureAwait(true);
            }
            catch (OperationCanceledException)
            {
                SetDeviceLog(device, DeviceLogResourceKeys.Ready);
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Failed to delete device {Serial}.", device.Serial);
                await ShowDeviceLogAsync(device, DeviceLogResourceKeys.DeleteDeviceFailed, CancellationToken.None).ConfigureAwait(true);
                SetDeviceLog(device, DeviceLogResourceKeys.Ready);
            }
        }

        [RelayCommand]
        private async Task RebootDeviceAsync(DeviceRowViewModel? device, CancellationToken cancellationToken)
        {
            if (device == null)
            {
                await ShowToolbarLogAsync(DeviceLogResourceKeys.SelectDeviceFirst, cancellationToken).ConfigureAwait(true);
                return;
            }

            SelectSingleDevice(device);
            SetDeviceLog(device, DeviceLogResourceKeys.RebootingDevice);

            try
            {
                await _deviceActionService.RebootAsync(device.Serial, cancellationToken).ConfigureAwait(true);
                await ShowDeviceLogAsync(device, DeviceLogResourceKeys.RebootDeviceSuccess, cancellationToken).ConfigureAwait(true);
                SetDeviceLog(device, DeviceLogResourceKeys.Ready);
            }
            catch (OperationCanceledException)
            {
                SetDeviceLog(device, DeviceLogResourceKeys.Ready);
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Failed to reboot device {Serial}.", device.Serial);
                await ShowDeviceLogAsync(device, DeviceLogResourceKeys.RebootDeviceFailed, CancellationToken.None).ConfigureAwait(true);
                SetDeviceLog(device, DeviceLogResourceKeys.Ready);
            }
        }

        [RelayCommand]
        private void ViewDeviceInfo(DeviceRowViewModel? device)
        {
            if (device != null)
                SelectSingleDevice(device);
        }

        [RelayCommand]
        private async Task RandomDeviceAsync(CancellationToken cancellationToken)
        {
            DeviceRowViewModel? device = SelectedDevice;
            if (device == null)
            {
                await ShowToolbarLogAsync(DeviceLogResourceKeys.SelectDeviceFirst, cancellationToken).ConfigureAwait(true);
                return;
            }

            SelectSingleDevice(device);

            var randomProfileLockTaken = false;
            try
            {
                await _randomProfileLock.WaitAsync(cancellationToken).ConfigureAwait(true);
                randomProfileLockTaken = true;
                SetDeviceLog(device, DeviceLogResourceKeys.RandomDevice);
                var randomResult = await _randomDeviceService
                    .CreateRandomProfileAsync(
                        new RandomDeviceRequest
                        {
                            SelectedBrand = SelectedBrand,
                            SelectedAndroidVersion = SelectedAndroidVersion,
                            UseIntegritySecurityPatch = UseIntegritySecurityPatch,
                            Country = SelectedCountry,
                            Carrier = SelectedCarrier
                        },
                        cancellationToken)
                    .ConfigureAwait(true);

                if (randomResult.Status == RandomDeviceStatus.LoginRequired)
                {
                    await ShowDeviceLogAsync(device, DeviceLogResourceKeys.RandomDeviceLoginRequired, cancellationToken).ConfigureAwait(true);
                    SetDeviceLog(device, DeviceLogResourceKeys.Ready);
                    return;
                }

                if (randomResult.Status == RandomDeviceStatus.Failed || randomResult.Profile == null)
                {
                    await ShowDeviceLogAsync(device, DeviceLogResourceKeys.RandomDeviceFailed, cancellationToken).ConfigureAwait(true);
                    SetDeviceLog(device, DeviceLogResourceKeys.Ready);
                    return;
                }

                ApplyRandomDeviceInfo(randomResult.Profile);
                await ShowDeviceLogAsync(device, DeviceLogResourceKeys.RandomDeviceSuccess, cancellationToken).ConfigureAwait(true);
                SetDeviceLog(device, DeviceLogResourceKeys.Ready);
            }
            catch (OperationCanceledException)
            {
                SetDeviceLog(device, DeviceLogResourceKeys.Ready);
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Unexpected failure while randomizing device info.");
                await ShowDeviceLogAsync(device, DeviceLogResourceKeys.RandomDeviceFailed, CancellationToken.None).ConfigureAwait(true);
                SetDeviceLog(device, DeviceLogResourceKeys.Ready);
            }
            finally
            {
                if (randomProfileLockTaken)
                    _randomProfileLock.Release();
            }
        }

        [RelayCommand]
        private async Task ChangeDeviceAsync(CancellationToken cancellationToken)
        {
            if (SelectedDevice == null)
            {
                await ShowToolbarLogAsync(DeviceLogResourceKeys.SelectDeviceFirst, cancellationToken).ConfigureAwait(true);
                return;
            }
            await ShowDeviceLogAsync(SelectedDevice, DeviceLogResourceKeys.ChangeDevice, cancellationToken).ConfigureAwait(true);
            SetDeviceLog(SelectedDevice, DeviceLogResourceKeys.Ready);
        }

        [RelayCommand]
        private async Task RandomAndChangeDeviceAsync(CancellationToken cancellationToken)
        {
            if (SelectedDevice == null)
            {
                await ShowToolbarLogAsync(DeviceLogResourceKeys.SelectDeviceFirst, cancellationToken).ConfigureAwait(true);
                return;
            }
            await ShowDeviceLogAsync(SelectedDevice, DeviceLogResourceKeys.RandomAndChangeDevice, cancellationToken).ConfigureAwait(true);
            SetDeviceLog(SelectedDevice, DeviceLogResourceKeys.Ready);
        }

        [RelayCommand]
        private async Task RandomSimAsync(CancellationToken cancellationToken)
        {
            DeviceRowViewModel? device = await GetSelectedOnlineDeviceAsync(cancellationToken).ConfigureAwait(true);
            if (device == null)
                return;

            var randomProfileLockTaken = false;
            try
            {
                await _randomProfileLock.WaitAsync(cancellationToken).ConfigureAwait(true);
                randomProfileLockTaken = true;
                SetDeviceLog(device, DeviceLogResourceKeys.RandomSim);
                SimProfile simProfile = _simProfileService.CreateRandomProfile(SelectedCountry, SelectedCarrier);
                ApplyRandomSimInfo(simProfile);
                await ShowDeviceLogAsync(device, DeviceLogResourceKeys.RandomSimSuccess, cancellationToken).ConfigureAwait(true);
                SetDeviceLog(device, DeviceLogResourceKeys.Ready);
            }
            catch (OperationCanceledException)
            {
                SetDeviceLog(device, DeviceLogResourceKeys.Ready);
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Failed to generate random SIM information.");
                await ShowDeviceLogAsync(device, DeviceLogResourceKeys.RandomSimFailed, CancellationToken.None).ConfigureAwait(true);
                SetDeviceLog(device, DeviceLogResourceKeys.Ready);
            }
            finally
            {
                if (randomProfileLockTaken)
                    _randomProfileLock.Release();
            }
        }

        private async Task<DeviceRowViewModel?> GetSelectedOnlineDeviceAsync(CancellationToken cancellationToken)
        {
            DeviceRowViewModel? device = SelectedDevice;
            if (device == null)
            {
                await ShowToolbarLogAsync(DeviceLogResourceKeys.SelectDeviceFirst, cancellationToken).ConfigureAwait(true);
                return null;
            }

            SelectSingleDevice(device);
            if (device.ConnectionStatus == AdbDeviceStatus.Online)
                return device;

            await ShowDeviceLogAsync(device, DeviceLogResourceKeys.DeviceMustBeOnline, cancellationToken).ConfigureAwait(true);
            SetDeviceLog(device, DeviceLogResourceKeys.Ready);
            return null;
        }

        [RelayCommand]
        private async Task ChangeSimAsync(CancellationToken cancellationToken)
        {
            if (SelectedDevice == null)
            {
                await ShowToolbarLogAsync(DeviceLogResourceKeys.SelectDeviceFirst, cancellationToken).ConfigureAwait(true);
                return;
            }
            await ShowDeviceLogAsync(SelectedDevice, DeviceLogResourceKeys.ChangeSim, cancellationToken).ConfigureAwait(true);
            SetDeviceLog(SelectedDevice, DeviceLogResourceKeys.Ready);
        }

        [RelayCommand]
        private async Task ChangeLocationAsync(CancellationToken cancellationToken)
        {
            if (SelectedDevice == null)
            {
                _logger.LogWarning("Change location requested but no device is selected.");
                await ShowToolbarLogAsync(DeviceLogResourceKeys.SelectDeviceFirst, cancellationToken).ConfigureAwait(true);
                return;
            }

            var device = SelectedDevice;
            SetDeviceLog(device, DeviceLogResourceKeys.OpeningDialog);

            try
            {
                var dialogResult = await _changeLocationDialogService
                    .ShowChangeLocationAsync(device.Serial, device.Name, cancellationToken)
                    .ConfigureAwait(true);

                if (dialogResult == null)
                {
                    await ShowDeviceLogAsync(device, DeviceLogResourceKeys.ChangeLocationCanceled, cancellationToken).ConfigureAwait(true);
                    SetDeviceLog(device, DeviceLogResourceKeys.Ready);
                    return;
                }

                SetDeviceLog(
                    device,
                    dialogResult.Mode == ChangeLocationMode.DeviceIp
                        ? DeviceLogResourceKeys.ResolvingByIp
                        : DeviceLogResourceKeys.ApplyingLocation);
                (string appliedLatitude, string appliedLongitude) = await _deviceLocationService
                    .ApplyAsync(device.Serial, dialogResult, cancellationToken)
                    .ConfigureAwait(true);

                await SaveLocationConfigAsync(
                        device.Serial,
                        dialogResult.Mode,
                        appliedLatitude,
                        appliedLongitude,
                        cancellationToken)
                    .ConfigureAwait(true);

                await ShowDeviceLogAsync(device, DeviceLogResourceKeys.ChangeLocationSuccess, cancellationToken).ConfigureAwait(true);
                SetDeviceLog(device, DeviceLogResourceKeys.Ready);
            }
            catch (OperationCanceledException)
            {
                await ShowDeviceLogAsync(device, DeviceLogResourceKeys.ChangeLocationCanceled, CancellationToken.None).ConfigureAwait(true);
                SetDeviceLog(device, DeviceLogResourceKeys.Ready);
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Failed to change location for device {Serial}.", device.Serial);
                await ShowDeviceLogAsync(device, DeviceLogResourceKeys.ChangeLocationFailed, CancellationToken.None).ConfigureAwait(true);
                SetDeviceLog(device, DeviceLogResourceKeys.Ready);
            }
        }

        [RelayCommand]
        private async Task ChangeTimezoneAsync(CancellationToken cancellationToken)
        {
            if (SelectedDevice == null)
            {
                _logger.LogWarning("Change timezone requested but no device is selected.");
                await ShowToolbarLogAsync(DeviceLogResourceKeys.SelectDeviceFirst, cancellationToken).ConfigureAwait(true);
                return;
            }

            var device = SelectedDevice;
            SetDeviceLog(device, DeviceLogResourceKeys.OpeningDialog);

            try
            {
                var dialogResult = await _changeTimezoneDialogService
                    .ShowChangeTimezoneAsync(device.Serial, device.Name, cancellationToken)
                    .ConfigureAwait(true);

                if (dialogResult == null)
                {
                    await ShowDeviceLogAsync(device, DeviceLogResourceKeys.ChangeTimezoneCanceled, cancellationToken).ConfigureAwait(true);
                    SetDeviceLog(device, DeviceLogResourceKeys.Ready);
                    return;
                }

                SetDeviceLog(
                    device,
                    dialogResult.Mode == ChangeTimezoneMode.DeviceIp
                        ? DeviceLogResourceKeys.ResolvingByIp
                        : DeviceLogResourceKeys.ApplyingTimezone);
                string appliedTimezone = await _deviceTimezoneService
                    .ApplyAsync(device.Serial, dialogResult, cancellationToken)
                    .ConfigureAwait(true);

                await SaveTimezoneConfigAsync(
                        device.Serial,
                        dialogResult.Mode,
                        appliedTimezone,
                        cancellationToken)
                    .ConfigureAwait(true);

                await ShowDeviceLogAsync(device, DeviceLogResourceKeys.ChangeTimezoneSuccess, cancellationToken).ConfigureAwait(true);
                SetDeviceLog(device, DeviceLogResourceKeys.Ready);
            }
            catch (OperationCanceledException)
            {
                await ShowDeviceLogAsync(device, DeviceLogResourceKeys.ChangeTimezoneCanceled, CancellationToken.None).ConfigureAwait(true);
                SetDeviceLog(device, DeviceLogResourceKeys.Ready);
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Failed to change timezone for device {Serial}.", device.Serial);
                await ShowDeviceLogAsync(device, DeviceLogResourceKeys.ChangeTimezoneFailed, CancellationToken.None).ConfigureAwait(true);
                SetDeviceLog(device, DeviceLogResourceKeys.Ready);
            }
        }

        private async Task SaveUpdateIntegrityConfigAsync(DeviceRowViewModel deviceRow, UpdateIntegrityDialogResult result, CancellationToken cancellationToken)
        {
            await _deviceRefreshLock.WaitAsync(cancellationToken).ConfigureAwait(true);
            try
            {
                await _deviceConfigService
                    .SaveUpdateIntegrityConfigAsync(_storedDevices, deviceRow.Serial, result, cancellationToken)
                    .ConfigureAwait(true);
            }
            finally
            {
                _deviceRefreshLock.Release();
            }
        }

        private bool CanViewRandomDeviceInfo()
        {
            return _lastRandomDeviceInfo != null;
        }

        [RelayCommand(CanExecute = nameof(CanViewRandomDeviceInfo))]
        private async Task ViewRandomDeviceInfoAsync(CancellationToken cancellationToken)
        {
            if (_lastRandomDeviceInfo == null)
                return;

            bool updated = await _randomDeviceInfoDialogService
                .ShowRandomDeviceInfoAsync(_lastRandomDeviceInfo, cancellationToken)
                .ConfigureAwait(true);
            if (updated)
                ApplyRandomDeviceInfo(_lastRandomDeviceInfo);
        }

        private async Task SaveLocationConfigAsync(
            string serial,
            ChangeLocationMode mode,
            string latitude,
            string longitude,
            CancellationToken cancellationToken)
        {
            await _deviceRefreshLock.WaitAsync(cancellationToken).ConfigureAwait(true);
            try
            {
                await _deviceConfigService
                    .SaveLocationConfigAsync(
                        _storedDevices,
                        serial,
                        mode,
                        latitude,
                        longitude,
                        cancellationToken)
                    .ConfigureAwait(true);
            }
            finally
            {
                _deviceRefreshLock.Release();
            }
        }

        private async Task SaveTimezoneConfigAsync(
            string serial,
            ChangeTimezoneMode mode,
            string timezone,
            CancellationToken cancellationToken)
        {
            await _deviceRefreshLock.WaitAsync(cancellationToken).ConfigureAwait(true);
            try
            {
                await _deviceConfigService
                    .SaveTimezoneConfigAsync(
                        _storedDevices,
                        serial,
                        mode,
                        timezone,
                        cancellationToken)
                    .ConfigureAwait(true);
            }
            finally
            {
                _deviceRefreshLock.Release();
            }
        }

        [RelayCommand]
        private async Task UpdateIntegrityAsync(CancellationToken cancellationToken)
        {
            if (SelectedDevice == null)
            {
                await ShowToolbarLogAsync(DeviceLogResourceKeys.SelectDeviceFirst, cancellationToken).ConfigureAwait(true);
                return;
            }

            var device = SelectedDevice;
            SetDeviceLog(device, DeviceLogResourceKeys.OpeningDialog);

            try
            {
                var storedDevice = _storedDevices.FirstOrDefault(d => SerialEquals(d.Serial, device.Serial));
                if (storedDevice == null)
                {
                    await ShowDeviceLogAsync(device, DeviceLogResourceKeys.UpdateIntegrityFailed, cancellationToken).ConfigureAwait(true);
                    SetDeviceLog(device, DeviceLogResourceKeys.Ready);
                    return;
                }

                var dialogResult = await _updateIntegrityDialogService
                    .ShowUpdateIntegrityAsync(
                        device.Serial,
                        device.Name,
                        storedDevice,
                        (result, saveCancellationToken) => SaveUpdateIntegrityConfigAsync(device, result, saveCancellationToken),
                        cancellationToken)
                    .ConfigureAwait(true);

                if (dialogResult == null)
                {
                    await ShowDeviceLogAsync(device, DeviceLogResourceKeys.UpdateIntegrityCanceled, cancellationToken).ConfigureAwait(true);
                    SetDeviceLog(device, DeviceLogResourceKeys.Ready);
                    return;
                }

                await SaveUpdateIntegrityConfigAsync(device, dialogResult, cancellationToken).ConfigureAwait(true);

                await ShowDeviceLogAsync(
                        device,
                        dialogResult.UpdateIntegrityEnabled
                            ? DeviceLogResourceKeys.UpdatingIntegrity
                            : DeviceLogResourceKeys.UpdatingKeybox,
                        cancellationToken)
                    .ConfigureAwait(true);
                await _deviceIntegrityService
                    .ApplyAsync(device.Serial, dialogResult, cancellationToken)
                    .ConfigureAwait(true);

                await ShowDeviceLogAsync(device, DeviceLogResourceKeys.UpdateIntegritySuccess, cancellationToken).ConfigureAwait(true);
                SetDeviceLog(device, DeviceLogResourceKeys.Ready);
            }
            catch (OperationCanceledException)
            {
                await ShowDeviceLogAsync(device, DeviceLogResourceKeys.UpdateIntegrityCanceled, CancellationToken.None).ConfigureAwait(true);
                SetDeviceLog(device, DeviceLogResourceKeys.Ready);
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Failed to update integrity for device {Serial}.", device.Serial);
                await ShowDeviceLogAsync(device, DeviceLogResourceKeys.UpdateIntegrityFailed, CancellationToken.None).ConfigureAwait(true);
                SetDeviceLog(device, DeviceLogResourceKeys.Ready);
            }
        }

        [RelayCommand]
        private async Task InstallApkAsync(CancellationToken cancellationToken)
        {
            if (SelectedDevice == null)
            {
                await ShowToolbarLogAsync(DeviceLogResourceKeys.SelectDeviceFirst, cancellationToken).ConfigureAwait(true);
                return;
            }

            var device = SelectedDevice;
            SetDeviceLog(device, DeviceLogResourceKeys.InstallPackageOpening);

            try
            {
                var dialogResult = await _installPackageDialogService
                    .ShowInstallPackageAsync(device.Serial, device.Name, cancellationToken)
                    .ConfigureAwait(true);

                if (dialogResult == null || dialogResult.TotalCount == 0)
                {
                    await ShowDeviceLogAsync(device, DeviceLogResourceKeys.InstallPackageCanceled, cancellationToken).ConfigureAwait(true);
                    SetDeviceLog(device, DeviceLogResourceKeys.Ready);
                    return;
                }

                string summaryKey = CreateInstallPackageSummaryKey(dialogResult);
                await ShowDeviceLogAsync(
                        device,
                        summaryKey,
                        cancellationToken,
                        dialogResult.SuccessCount,
                        dialogResult.TotalCount)
                    .ConfigureAwait(true);
                SetDeviceLog(device, DeviceLogResourceKeys.Ready);
            }
            catch (OperationCanceledException)
            {
                await ShowDeviceLogAsync(device, DeviceLogResourceKeys.InstallPackageCanceled, CancellationToken.None).ConfigureAwait(true);
                SetDeviceLog(device, DeviceLogResourceKeys.Ready);
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Failed to install package for selected device.");
                await ShowDeviceLogAsync(device, DeviceLogResourceKeys.InstallPackageAdbFailure, CancellationToken.None).ConfigureAwait(true);
                SetDeviceLog(device, DeviceLogResourceKeys.Ready);
            }
        }

        private static string CreateInstallPackageSummaryKey(InstallPackageDialogResult result)
        {
            if (result.Canceled)
                return DeviceLogResourceKeys.InstallPackageCanceled;

            if (result.FailedCount == 0 && result.SuccessCount == result.TotalCount)
                return DeviceLogResourceKeys.InstallPackageCompleteFormat;

            if (result.SuccessCount > 0)
                return DeviceLogResourceKeys.InstallPackagePartialFormat;

            return DeviceLogResourceKeys.InstallPackageFailedFormat;
        }

        [RelayCommand]
        private async Task FakeProxyAsync(CancellationToken cancellationToken)
        {
            if (SelectedDevice == null)
            {
                _logger.LogWarning("Fake proxy requested but no device is selected.");
                await ShowToolbarLogAsync(DeviceLogResourceKeys.SelectDeviceFirst, cancellationToken).ConfigureAwait(true);
                return;
            }

            var device = SelectedDevice;
            SetDeviceLog(device, DeviceLogResourceKeys.OpeningDialog);

            try
            {
                var dialogResult = await _fakeProxyDialogService
                    .ShowFakeProxyDialogAsync(device.Serial, device.Name, cancellationToken)
                    .ConfigureAwait(true);

                if (dialogResult == null)
                {
                    await ShowDeviceLogAsync(device, DeviceLogResourceKeys.FakeProxyCanceled, cancellationToken).ConfigureAwait(true);
                    SetDeviceLog(device, DeviceLogResourceKeys.Ready);
                    return;
                }

                SetDeviceLog(device, DeviceLogResourceKeys.StartingProxy);

                ProxyWorkflowResult workflowResult = await _proxyWorkflowService
                    .ApplyAsync(device.Serial, dialogResult, cancellationToken)
                    .ConfigureAwait(true);

                if (!string.IsNullOrWhiteSpace(workflowResult.AppliedLatitude)
                    && !string.IsNullOrWhiteSpace(workflowResult.AppliedLongitude))
                {
                    await SaveLocationConfigAsync(
                            device.Serial,
                            ChangeLocationMode.DeviceIp,
                            workflowResult.AppliedLatitude,
                            workflowResult.AppliedLongitude,
                            cancellationToken)
                        .ConfigureAwait(true);
                }

                if (!string.IsNullOrWhiteSpace(workflowResult.AppliedTimezone))
                {
                    await SaveTimezoneConfigAsync(
                            device.Serial,
                            ChangeTimezoneMode.DeviceIp,
                            workflowResult.AppliedTimezone,
                            cancellationToken)
                        .ConfigureAwait(true);

                }

                if (workflowResult.LocationUpdateFailed)
                {
                    await ShowDeviceLogAsync(
                            device,
                            DeviceLogResourceKeys.ProxyLocationByIpFailed,
                            cancellationToken)
                        .ConfigureAwait(true);
                }

                if (workflowResult.TimezoneUpdateFailed)
                {
                    await ShowDeviceLogAsync(
                            device,
                            DeviceLogResourceKeys.ProxyTimezoneByIpFailed,
                            cancellationToken)
                        .ConfigureAwait(true);
                }

                bool postProxyUpdatesSucceeded =
                    !workflowResult.LocationUpdateFailed && !workflowResult.TimezoneUpdateFailed;

                await ShowDeviceLogAsync(
                        device,
                        postProxyUpdatesSucceeded
                            ? DeviceLogResourceKeys.FakeProxySuccess
                            : DeviceLogResourceKeys.FakeProxyPartialSuccess,
                        cancellationToken)
                    .ConfigureAwait(true);
                SetDeviceLog(device, DeviceLogResourceKeys.Ready);
            }
            catch (OperationCanceledException)
            {
                await ShowDeviceLogAsync(device, DeviceLogResourceKeys.FakeProxyCanceled, CancellationToken.None).ConfigureAwait(true);
                SetDeviceLog(device, DeviceLogResourceKeys.Ready);
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Failed to apply fake proxy for device {Serial}.", device.Serial);
                await ShowDeviceLogAsync(device, DeviceLogResourceKeys.FakeProxyFailed, CancellationToken.None).ConfigureAwait(true);
                SetDeviceLog(device, DeviceLogResourceKeys.Ready);
            }
        }

        [RelayCommand]
        private async Task StopFakeProxyAsync(CancellationToken cancellationToken)
        {
            if (SelectedDevice == null)
            {
                _logger.LogWarning("Stop fake proxy requested but no device is selected.");
                await ShowToolbarLogAsync(DeviceLogResourceKeys.SelectDeviceFirst, cancellationToken).ConfigureAwait(true);
                return;
            }

            var device = SelectedDevice;
            SetDeviceLog(device, DeviceLogResourceKeys.StoppingProxy);

            try
            {
                await _adbProxyService
                    .StopProxyAsync(device.Serial, cancellationToken)
                    .ConfigureAwait(true);

                await ShowDeviceLogAsync(device, DeviceLogResourceKeys.StopFakeProxySuccess, cancellationToken).ConfigureAwait(true);
                SetDeviceLog(device, DeviceLogResourceKeys.Ready);
            }
            catch (OperationCanceledException)
            {
                SetDeviceLog(device, DeviceLogResourceKeys.Ready);
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Failed to stop fake proxy for device {Serial}.", device.Serial);
                await ShowDeviceLogAsync(device, DeviceLogResourceKeys.StopFakeProxyFailed, CancellationToken.None).ConfigureAwait(true);
                SetDeviceLog(device, DeviceLogResourceKeys.Ready);
            }
        }

        [RelayCommand]
        private async Task ViewDeviceAsync(DeviceRowViewModel? device, CancellationToken cancellationToken)
        {
            var targetDevice = device ?? SelectedDevice;
            if (targetDevice == null)
            {
                await ShowToolbarLogAsync(DeviceLogResourceKeys.SelectDeviceFirst, cancellationToken).ConfigureAwait(true);
                return;
            }

            SetDeviceLog(targetDevice, DeviceLogResourceKeys.OpeningDialog);

            try
            {
                await _deviceViewerDialogService.ShowDeviceViewerAsync(targetDevice.Serial, targetDevice.Name, cancellationToken).ConfigureAwait(true);
                SetDeviceLog(targetDevice, DeviceLogResourceKeys.Ready);
            }
            catch (OperationCanceledException)
            {
                SetDeviceLog(targetDevice, DeviceLogResourceKeys.Ready);
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Failed to open device viewer for device {Serial}.", targetDevice.Serial);
                SetDeviceLog(targetDevice, DeviceLogResourceKeys.Ready);
            }
        }

        private async Task LoadCarrierProfilesAsync(CancellationToken cancellationToken)
        {
            var carrierProfiles = await _carrierDataService.GetCarrierProfilesAsync(cancellationToken).ConfigureAwait(false);
            await RunOnUiContextAsync(() =>
            {
                _carrierProfiles = carrierProfiles.ToList();
                RefreshCountryOptions();
                ApplyStoredDeviceConfig(SelectedDevice);
            }).ConfigureAwait(false);
        }

        private async Task LoadSavedDevicesAsync(CancellationToken cancellationToken)
        {
            await _deviceRefreshLock.WaitAsync(cancellationToken).ConfigureAwait(false);

            try
            {
                _storedDevices = (await _deviceListService.LoadStoredDevicesAsync(cancellationToken).ConfigureAwait(false)).ToList();
                await RunOnUiContextAsync(() =>
                {
                    RefreshDeviceRows(_storedDevices, Array.Empty<AdbDevice>());
                }).ConfigureAwait(false);
            }
            finally
            {
                _deviceRefreshLock.Release();
            }
        }

        private async Task SaveSettingsAsync(CancellationToken cancellationToken)
        {
            try
            {
                await _deviceConfigService.SaveSettingsAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to save application settings.");
            }
        }

        [RelayCommand]
        private async Task SaveColumnRatiosAsync(
            IReadOnlyDictionary<string, double>? ratios,
            CancellationToken cancellationToken)
        {
            if (ratios == null || ratios.Count == 0)
                return;

            _settings.DeviceTableColumnRatios = new Dictionary<string, double>(ratios, StringComparer.Ordinal);
            OnPropertyChanged(nameof(DeviceTableColumnRatios));
            await SaveSettingsAsync(cancellationToken).ConfigureAwait(false);
        }

        private async Task RefreshNewDeviceCountAsync(CancellationToken cancellationToken)
        {
            if (_isShowingToolbarLog)
                return;

            if (!await _deviceRefreshLock.WaitAsync(0, cancellationToken).ConfigureAwait(false))
                return;

            try
            {
                var snapshot = await _deviceListService.LoadSnapshotAsync(cancellationToken).ConfigureAwait(false);
                _storedDevices = snapshot.StoredDevices.ToList();

                var newDeviceCount = _deviceListService.CountNewDevices(snapshot.StoredDevices, snapshot.ConnectedDevices);
                await RunOnUiContextAsync(() =>
                {
                    UpdateDeviceConnectionStatuses(snapshot.ConnectedDevices);
                    NewDeviceCountText = string.Format(
                        _localizationService.GetString("DeviceManager_NewDeviceCount"),
                        newDeviceCount);
                }).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Failed to refresh the new-device count.");
                await RunOnUiContextAsync(() =>
                {
                    NewDeviceCountText = string.Format(
                        _localizationService.GetString("DeviceManager_NewDeviceCount"),
                        _localizationService.GetString("DeviceManager_NotAvailable"));
                }).ConfigureAwait(false);
            }
            finally
            {
                _deviceRefreshLock.Release();
            }
        }

        private void UpdateDeviceConnectionStatuses(IReadOnlyList<AdbDevice> connectedDevices)
        {
            var connectedBySerial = connectedDevices.ToDictionary(device => device.Serial, StringComparer.OrdinalIgnoreCase);

            foreach (var device in _allDeviceRows)
            {
                device.ConnectionStatus = connectedBySerial.TryGetValue(device.Serial, out var connectedDevice)
                    ? connectedDevice.Status
                    : AdbDeviceStatus.Offline;
                device.Status = GetConnectionStatusText(device.ConnectionStatus);
            }

            ApplyDeviceFilter();
        }

        private void RefreshDeviceRows(IReadOnlyList<StoredDeviceConfig> storedDevices, IReadOnlyList<AdbDevice> connectedDevices)
        {
            _isRefreshingRows = true;
            var targetSerial = SelectedDevice?.Serial ?? _settings.SelectedDeviceSerial;

            try
            {
                foreach (var device in _allDeviceRows)
                    device.PropertyChanged -= OnDeviceRowPropertyChanged;

                _allDeviceRows.Clear();

                var connectedBySerial = connectedDevices.ToDictionary(device => device.Serial, StringComparer.OrdinalIgnoreCase);

                for (var index = 0; index < storedDevices.Count; index++)
                {
                    var storedDevice = storedDevices[index];
                    connectedBySerial.TryGetValue(storedDevice.Serial, out var connectedDevice);
                    var deviceRow = CreateDeviceRow(index + 1, storedDevice, connectedDevice);
                    deviceRow.PropertyChanged += OnDeviceRowPropertyChanged;
                    _allDeviceRows.Add(deviceRow);
                }

                ApplyDeviceFilterCore();
            }
            finally
            {
                _isRefreshingRows = false;
            }

            RestoreSelection(targetSerial);
        }

        internal void ApplyDeviceListSnapshot(DeviceListSnapshot snapshot)
        {
            _storedDevices = snapshot.StoredDevices.ToList();
            RefreshDeviceRows(_storedDevices, snapshot.ConnectedDevices);
        }

        private void ApplyDeviceFilter()
        {
            _isRefreshingRows = true;
            var targetSerial = SelectedDevice?.Serial ?? _settings.SelectedDeviceSerial;

            try
            {
                ApplyDeviceFilterCore();
            }
            finally
            {
                _isRefreshingRows = false;
            }

            RestoreSelection(targetSerial);
        }

        private void RestoreSelection(string targetSerial)
        {
            string? selectedSerial = _deviceSelectionService.FindSelectionSerial(
                targetSerial,
                Devices.Select(device => device.Serial).ToList(),
                _allDeviceRows.Select(device => device.Serial).ToList());
            DeviceRowViewModel? selectedDevice = selectedSerial == null
                ? null
                : _allDeviceRows.FirstOrDefault(device => SerialEquals(device.Serial, selectedSerial));
            SelectSingleDevice(selectedDevice);
        }

        private void ApplyDeviceFilterCore()
        {
            var visibleDevices = _allDeviceRows
                .Where(MatchesDeviceFilter)
                .ToList();

            for (var index = Devices.Count - 1; index >= 0; index--)
            {
                if (!visibleDevices.Contains(Devices[index]))
                    Devices.RemoveAt(index);
            }

            for (var targetIndex = 0; targetIndex < visibleDevices.Count; targetIndex++)
            {
                var device = visibleDevices[targetIndex];
                var currentIndex = Devices.IndexOf(device);

                if (currentIndex < 0)
                    Devices.Insert(targetIndex, device);
                else if (currentIndex != targetIndex)
                    Devices.Move(currentIndex, targetIndex);
            }
        }

        private bool MatchesDeviceFilter(DeviceRowViewModel device)
        {
            return SelectedDeviceFilter switch
            {
                DeviceFilterKeys.Online => device.ConnectionStatus == AdbDeviceStatus.Online,
                DeviceFilterKeys.Offline => device.ConnectionStatus != AdbDeviceStatus.Online,
                DeviceFilterKeys.Active => string.Equals(device.Active, DeviceFilterKeys.Active, StringComparison.OrdinalIgnoreCase),
                DeviceFilterKeys.Inactive => string.Equals(device.Active, DeviceFilterKeys.Inactive, StringComparison.OrdinalIgnoreCase),
                _ => true
            };
        }

        private void OnDeviceRowPropertyChanged(object? sender, PropertyChangedEventArgs args)
        {
            if (_isRefreshingRows || sender is not DeviceRowViewModel deviceRow)
                return;

            if (args.PropertyName == nameof(DeviceRowViewModel.IsSelected))
            {
                if (deviceRow.IsSelected && !_isSynchronizingSelection)
                    SelectSingleDevice(deviceRow);

                return;
            }

            if (args.PropertyName == nameof(DeviceRowViewModel.Name))
            {
                QueueDeviceRowEdit(deviceRow);
                return;
            }

            if (args.PropertyName == nameof(DeviceRowViewModel.Type))
            {
                CancelPendingDeviceEdit(deviceRow.Serial);
                TrackSilentSave(SaveDeviceRowEditAsync(deviceRow, GetActiveToken()), "Failed to save device row edit.");
            }
        }

        private void SelectSingleDevice(DeviceRowViewModel? selectedDevice)
        {
            var previousDevice = _selectedDevice;
            var previousSerial = previousDevice?.Serial ?? string.Empty;
            var selectedSerial = selectedDevice?.Serial ?? string.Empty;
            var serialChanged = !SerialEquals(previousSerial, selectedSerial);
            var referenceChanged = !ReferenceEquals(previousDevice, selectedDevice);

            _isSynchronizingSelection = true;
            try
            {
                foreach (DeviceRowViewModel device in Devices)
                    device.IsSelected = ReferenceEquals(device, selectedDevice);

                SetProperty(ref _selectedDevice, selectedDevice, nameof(SelectedDevice));
            }
            finally
            {
                _isSynchronizingSelection = false;
            }

            if (serialChanged)
            {
                _settings.SelectedDeviceSerial = selectedSerial;
                TrackSilentSave(SaveSettingsAsync(GetActiveToken()), "Failed to save selected device setting.");
            }

            if (referenceChanged)
                ApplyStoredDeviceConfig(selectedDevice);
        }

        private void QueueDeviceRowEdit(DeviceRowViewModel deviceRow)
        {
            var cancellation = new CancellationTokenSource();
            var pendingEdit = new PendingDeviceEdit(deviceRow, cancellation);

            lock (_pendingDeviceEditsLock)
            {
                if (_pendingDeviceEdits.Remove(deviceRow.Serial, out var previousEdit))
                    previousEdit.Cancellation.Cancel();

                _pendingDeviceEdits[deviceRow.Serial] = pendingEdit;
                pendingEdit.PersistenceTask = PersistDeviceRowEditAfterDelayAsync(pendingEdit);
            }
        }

        private async Task PersistDeviceRowEditAfterDelayAsync(PendingDeviceEdit pendingEdit)
        {
            try
            {
                await Task.Delay(DeviceNameSaveDebounceMilliseconds, pendingEdit.Cancellation.Token).ConfigureAwait(false);
                await SaveDeviceRowEditAsync(pendingEdit.DeviceRow, pendingEdit.Cancellation.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (pendingEdit.Cancellation.IsCancellationRequested)
            {
            }
            finally
            {
                lock (_pendingDeviceEditsLock)
                {
                    if (_pendingDeviceEdits.TryGetValue(pendingEdit.DeviceRow.Serial, out var currentEdit)
                        && ReferenceEquals(currentEdit, pendingEdit))
                    {
                        _pendingDeviceEdits.Remove(pendingEdit.DeviceRow.Serial);
                    }
                }

                pendingEdit.Cancellation.Dispose();
            }
        }

        private void CancelPendingDeviceEdit(string serial)
        {
            lock (_pendingDeviceEditsLock)
            {
                if (_pendingDeviceEdits.Remove(serial, out var pendingEdit))
                    pendingEdit.Cancellation.Cancel();
            }
        }

        private async Task FlushPendingDeviceEditsAsync()
        {
            PendingDeviceEdit[] pendingEdits;
            lock (_pendingDeviceEditsLock)
            {
                pendingEdits = _pendingDeviceEdits.Values.ToArray();
                _pendingDeviceEdits.Clear();
                foreach (var pendingEdit in pendingEdits)
                    pendingEdit.Cancellation.Cancel();
            }

            if (pendingEdits.Length == 0)
                return;

            await Task.WhenAll(pendingEdits.Select(edit => edit.PersistenceTask)).ConfigureAwait(false);
            foreach (var pendingEdit in pendingEdits)
                await SaveDeviceRowEditAsync(pendingEdit.DeviceRow, CancellationToken.None).ConfigureAwait(false);
        }

        private void RefreshCountryOptions()
        {
            Countries.Clear();

            foreach (var country in _carrierProfiles
                         .GroupBy(profile => profile.CountryIso, StringComparer.OrdinalIgnoreCase)
                         .Select(group => group.First())
                         .OrderBy(profile => profile.CountryName, StringComparer.OrdinalIgnoreCase)
                         .ThenBy(profile => profile.CountryIso, StringComparer.OrdinalIgnoreCase))
            {
                Countries.Add(new CarrierCountryOption(country.CountryIso, country.CountryCode, country.CountryName));
            }
        }

        private void ApplyStoredDeviceConfig(DeviceRowViewModel? selectedDevice)
        {
            _isApplyingDeviceConfig = true;

            try
            {
                var storedDevice = selectedDevice == null
                    ? null
                    : _storedDevices.FirstOrDefault(device => SerialEquals(device.Serial, selectedDevice.Serial));
                var selectedCountry = FindCountryOption(storedDevice?.CountryIso)
                    ?? FindCountryOptionByName(storedDevice?.CountryName)
                    ?? FindCountryOption(DefaultCountryIso)
                    ?? Countries.FirstOrDefault();

                SelectedBrand = FindOption(Brands, storedDevice?.Brand) ?? DeviceProfileOptions.Random;
                UpdateAndroidVersionOptions(SelectedBrand, storedDevice?.AndroidVersion);
                IsChangeSimEnabled = storedDevice?.ChangeSimEnabled ?? true;
                UseIntegritySecurityPatch = storedDevice?.UseIntegritySecurityPatch ?? false;
                SelectedCountry = selectedCountry;
                UpdateCarrierOptionsForCountry(selectedCountry?.CountryIso, storedDevice);
            }
            finally
            {
                _isApplyingDeviceConfig = false;
            }
        }

        private void UpdateCarrierOptionsForCountry(string? countryIso, StoredDeviceConfig? storedDevice)
        {
            _isUpdatingCarrierOptions = true;

            try
            {
                Carriers.Clear();
                var targetCountryIso = string.IsNullOrWhiteSpace(countryIso)
                    ? DefaultCountryIso
                    : countryIso.Trim().ToLowerInvariant();
                var carrierOptions = _carrierProfiles
                    .Where(profile => SerialEquals(profile.CountryIso, targetCountryIso))
                    .Select(profile => new CarrierOption(profile.CarrierName, profile.Mcc, profile.Mnc))
                    .OrderBy(carrier => carrier.CarrierName, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(carrier => carrier.Mcc, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(carrier => carrier.Mnc, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                foreach (var carrierOption in carrierOptions)
                    Carriers.Add(carrierOption);

                SelectedCarrier = FindCarrierOption(storedDevice) ?? Carriers.FirstOrDefault();
            }
            finally
            {
                _isUpdatingCarrierOptions = false;
            }
        }

        private CarrierCountryOption? FindCountryOption(string? countryIso)
        {
            return string.IsNullOrWhiteSpace(countryIso)
                ? null
                : Countries.FirstOrDefault(country => SerialEquals(country.CountryIso, countryIso.Trim()));
        }

        private CarrierCountryOption? FindCountryOptionByName(string? countryName)
        {
            return string.IsNullOrWhiteSpace(countryName)
                ? null
                : Countries.FirstOrDefault(country => string.Equals(country.CountryName, countryName.Trim(), StringComparison.OrdinalIgnoreCase));
        }

        private CarrierOption? FindCarrierOption(StoredDeviceConfig? storedDevice)
        {
            if (storedDevice == null || string.IsNullOrWhiteSpace(storedDevice.Carrier))
                return null;

            var carrierName = storedDevice.Carrier.Trim();
            var carrierMcc = storedDevice.CarrierMcc.Trim();
            var carrierMnc = storedDevice.CarrierMnc.Trim();

            if (carrierMcc.Length > 0 || carrierMnc.Length > 0)
            {
                var exactCarrier = Carriers.FirstOrDefault(carrier =>
                    string.Equals(carrier.CarrierName, carrierName, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(carrier.Mcc, carrierMcc, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(carrier.Mnc, carrierMnc, StringComparison.OrdinalIgnoreCase));
                if (exactCarrier != null)
                    return exactCarrier;
            }

            return Carriers.FirstOrDefault(carrier => string.Equals(carrier.CarrierName, carrierName, StringComparison.OrdinalIgnoreCase));
        }

        private static string? FindOption(IReadOnlyList<string> options, string? value)
        {
            return string.IsNullOrWhiteSpace(value)
                ? null
                : options.FirstOrDefault(option => string.Equals(option, value.Trim(), StringComparison.OrdinalIgnoreCase));
        }

        private void UpdateAndroidVersionOptions(string? brand, string? preferredVersion)
        {
            IReadOnlyList<string> compatibleVersions = string.IsNullOrWhiteSpace(brand)
                || string.Equals(brand, DeviceProfileOptions.Random, StringComparison.OrdinalIgnoreCase)
                || !DeviceProfileOptions.AndroidVersionsByBrand.TryGetValue(brand, out IReadOnlyList<string>? versions)
                    ? DeviceProfileOptions.SupportedAndroidVersions
                    : versions;

            AndroidVersions.Clear();
            AndroidVersions.Add(DeviceProfileOptions.Random);
            foreach (string version in compatibleVersions)
                AndroidVersions.Add(version);

            SelectedAndroidVersion = FindOption(AndroidVersions, preferredVersion)
                ?? DeviceProfileOptions.Random;
        }

        private void QueueSelectedDeviceProfileSave()
        {
            DeviceRowViewModel? selectedDevice = SelectedDevice;
            if (selectedDevice == null || _isApplyingDeviceConfig)
                return;

            var pendingEdit = new PendingDeviceProfileEdit(
                selectedDevice.Serial,
                CreateDeviceProfileConfig(),
                new CancellationTokenSource());

            lock (_pendingProfileEditLock)
            {
                if (_pendingProfileEdits.Remove(selectedDevice.Serial, out var previousEdit))
                    previousEdit.Cancellation.Cancel();

                _pendingProfileEdits[selectedDevice.Serial] = pendingEdit;
                pendingEdit.PersistenceTask = PersistDeviceProfileAfterDelayAsync(pendingEdit);
            }
        }

        private DeviceProfileConfig CreateDeviceProfileConfig()
        {
            return new DeviceProfileConfig
            {
                Brand = SelectedBrand ?? string.Empty,
                AndroidVersion = SelectedAndroidVersion ?? string.Empty,
                ChangeSimEnabled = IsChangeSimEnabled,
                UseIntegritySecurityPatch = UseIntegritySecurityPatch,
                CountryIso = SelectedCountry?.CountryIso ?? string.Empty,
                CountryName = SelectedCountry?.CountryName ?? string.Empty,
                Carrier = SelectedCarrier?.CarrierName ?? string.Empty,
                CarrierMcc = SelectedCarrier?.Mcc ?? string.Empty,
                CarrierMnc = SelectedCarrier?.Mnc ?? string.Empty
            };
        }

        private async Task PersistDeviceProfileAfterDelayAsync(PendingDeviceProfileEdit pendingEdit)
        {
            try
            {
                await Task.Delay(DeviceNameSaveDebounceMilliseconds, pendingEdit.Cancellation.Token).ConfigureAwait(false);
                await SaveDeviceProfileAsync(pendingEdit, pendingEdit.Cancellation.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (pendingEdit.Cancellation.IsCancellationRequested)
            {
            }
            finally
            {
                lock (_pendingProfileEditLock)
                {
                    if (_pendingProfileEdits.TryGetValue(pendingEdit.Serial, out var currentEdit)
                        && ReferenceEquals(currentEdit, pendingEdit))
                    {
                        _pendingProfileEdits.Remove(pendingEdit.Serial);
                    }
                }

                pendingEdit.Cancellation.Dispose();
            }
        }

        private async Task SaveDeviceProfileAsync(PendingDeviceProfileEdit pendingEdit, CancellationToken cancellationToken)
        {
            try
            {
                await _deviceRefreshLock.WaitAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    await _deviceConfigService
                        .SaveDeviceProfileAsync(
                            _storedDevices,
                            pendingEdit.Serial,
                            pendingEdit.Profile,
                            cancellationToken)
                        .ConfigureAwait(false);
                }
                finally
                {
                    _deviceRefreshLock.Release();
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Failed to save device profile config.");
            }
        }

        private async Task FlushPendingDeviceProfileAsync()
        {
            PendingDeviceProfileEdit[] pendingEdits;
            lock (_pendingProfileEditLock)
            {
                pendingEdits = _pendingProfileEdits.Values.ToArray();
                _pendingProfileEdits.Clear();
                foreach (var pendingEdit in pendingEdits)
                    pendingEdit.Cancellation.Cancel();
            }

            if (pendingEdits.Length == 0)
                return;

            await Task.WhenAll(pendingEdits.Select(edit => edit.PersistenceTask)).ConfigureAwait(false);
            foreach (var pendingEdit in pendingEdits)
                await SaveDeviceProfileAsync(pendingEdit, CancellationToken.None).ConfigureAwait(false);
        }

        private async Task SaveDeviceRowEditAsync(DeviceRowViewModel deviceRow, CancellationToken cancellationToken)
        {
            await SaveDeviceConfigAsync(deviceRow, ReferenceEquals(deviceRow, SelectedDevice), cancellationToken).ConfigureAwait(false);
        }

        private async Task SaveDeviceConfigAsync(DeviceRowViewModel deviceRow, bool includeSelectedCarrierConfig, CancellationToken cancellationToken)
        {
            try
            {
                await _deviceRefreshLock.WaitAsync(cancellationToken).ConfigureAwait(false);

                try
                {
                    await _deviceConfigService
                        .SaveDeviceRowAsync(
                            _storedDevices,
                            deviceRow.Serial,
                            deviceRow.Name,
                            deviceRow.Type,
                            SelectedCountry,
                            SelectedCarrier,
                            includeSelectedCarrierConfig,
                            cancellationToken)
                        .ConfigureAwait(false);
                }
                finally
                {
                    _deviceRefreshLock.Release();
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Failed to save device row edit.");
            }
        }

        private DeviceRowViewModel CreateDeviceRow(int index, StoredDeviceConfig storedDevice, AdbDevice? connectedDevice)
        {
            AdbDeviceStatus connectionStatus = connectedDevice?.Status ?? AdbDeviceStatus.Offline;
            return DeviceRowFactory.CreateDeviceRow(
                index,
                storedDevice,
                connectedDevice,
                GetConnectionStatusText(connectionStatus),
                GetLogText(DeviceLogResourceKeys.Ready));
        }

        private string GetConnectionStatusText(AdbDeviceStatus status)
        {
            string resourceKey = status switch
            {
                AdbDeviceStatus.Online => "DeviceManager_StatusOnline",
                AdbDeviceStatus.Unauthorized => "DeviceManager_StatusUnauthorized",
                _ => "DeviceManager_StatusOffline"
            };
            return _localizationService.GetString(resourceKey);
        }

        private static bool SerialEquals(string left, string right)
        {
            return DeviceRowFactory.SerialEquals(left, right);
        }

        private Task RunOnUiContextAsync(Action action)
        {
            return _uiDispatcher.InvokeAsync(action);
        }

        private CancellationToken GetActiveToken()
        {
            return _pollCancellation?.Token ?? CancellationToken.None;
        }

        private void TrackSilentSave(Task saveTask, string failureMessage)
        {
            _ = ObserveSilentSaveAsync(saveTask, failureMessage);
        }

        private async Task ObserveSilentSaveAsync(Task saveTask, string failureMessage)
        {
            try
            {
                await saveTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, failureMessage);
            }
        }

        private void ApplyRandomDeviceInfo(DeviceInfoApiDevice randomDevice)
        {
            _lastRandomDeviceInfo = randomDevice;
            ViewRandomDeviceInfoCommand.NotifyCanExecuteChanged();
            DeviceInfo.Name = GetFirstValue(randomDevice.Name, randomDevice.Board, randomDevice.Code);
            DeviceInfo.Model = randomDevice.Model ?? string.Empty;
            DeviceInfo.Brand = GetFirstValue(randomDevice.Brand, randomDevice.Manufacturer);
            DeviceInfo.AndroidVersion = GetAndroidVersionDisplay(randomDevice.Release, randomDevice.Sdk);
            DeviceInfo.Serial = randomDevice.Serial;
            DeviceInfo.Imei = randomDevice.Imei ?? string.Empty;
            DeviceInfo.Iccid = randomDevice.Iccid;
            DeviceInfo.Imsi = randomDevice.Imsi;
            DeviceInfo.Operator = string.IsNullOrWhiteSpace(randomDevice.SimOperatorName)
                ? randomDevice.SimOperatorNumeric
                : randomDevice.SimOperatorName;
            DeviceInfo.PhoneNumber = randomDevice.SimPhoneNumber;
            DeviceInfo.Mac = randomDevice.WifiMacAddress;
        }

        private void ApplyRandomSimInfo(SimProfile simProfile)
        {
            DeviceInfo.Iccid = simProfile.Iccid;
            DeviceInfo.Imsi = simProfile.Imsi;
            DeviceInfo.Operator = string.IsNullOrWhiteSpace(simProfile.OperatorName)
                ? simProfile.OperatorNumeric
                : simProfile.OperatorName;
            DeviceInfo.PhoneNumber = simProfile.PhoneNumber;

            if (_lastRandomDeviceInfo == null)
                return;

            _lastRandomDeviceInfo.Iccid = simProfile.Iccid;
            _lastRandomDeviceInfo.Imsi = simProfile.Imsi;
            _lastRandomDeviceInfo.SimPhoneNumber = simProfile.PhoneNumber;
            _lastRandomDeviceInfo.SimOperatorNumeric = simProfile.OperatorNumeric;
            _lastRandomDeviceInfo.SimOperatorCountry = simProfile.OperatorCountry;
            _lastRandomDeviceInfo.SimOperatorName = simProfile.OperatorName;
        }

        private static string GetAndroidVersionDisplay(string? release, string? sdk)
        {
            if (!string.IsNullOrWhiteSpace(release))
                return release.StartsWith("Android ", StringComparison.OrdinalIgnoreCase)
                    ? release
                    : string.Concat("Android ", release.Trim());

            return sdk?.Trim() switch
            {
                "33" => DeviceProfileOptions.Android13,
                "34" => DeviceProfileOptions.Android14,
                "35" => DeviceProfileOptions.Android15,
                _ => string.Empty
            };
        }

        private static string GetFirstValue(params string?[] values)
        {
            return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;
        }

        private static DeviceInfoFormViewModel CreateDefaultDeviceInfo()
        {
            return new DeviceInfoFormViewModel
            {
                Name = string.Empty,
                Model = string.Empty,
                Brand = string.Empty,
                AndroidVersion = string.Empty,
                Serial = string.Empty,
                Imei = string.Empty,
                Iccid = string.Empty,
                Imsi = string.Empty,
                Operator = string.Empty,
                PhoneNumber = string.Empty,
                Mac = string.Empty,
                Latitude = string.Empty,
                Longitude = string.Empty
            };
        }

        private sealed class PendingDeviceEdit
        {
            public PendingDeviceEdit(DeviceRowViewModel deviceRow, CancellationTokenSource cancellation)
            {
                DeviceRow = deviceRow;
                Cancellation = cancellation;
            }

            public DeviceRowViewModel DeviceRow { get; }
            public CancellationTokenSource Cancellation { get; }
            public Task PersistenceTask { get; set; } = Task.CompletedTask;
        }

        private sealed class PendingDeviceProfileEdit
        {
            public PendingDeviceProfileEdit(
                string serial,
                DeviceProfileConfig profile,
                CancellationTokenSource cancellation)
            {
                Serial = serial;
                Profile = profile;
                Cancellation = cancellation;
            }

            public string Serial { get; }
            public DeviceProfileConfig Profile { get; }
            public CancellationTokenSource Cancellation { get; }
            public Task PersistenceTask { get; set; } = Task.CompletedTask;
        }
    }
}
