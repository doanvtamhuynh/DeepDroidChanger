using DeepDroidChanger.Services;
using DeepDroidChanger.Models;
using DeepDroidChanger.Helpers;
using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace DeepDroidChanger.ViewModels
{
    public sealed partial class ChangeSingleDeviceViewModel : ObservableObject, IDisposable
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
        private readonly IPackageInstallService _packageInstallService;
        private readonly IDeviceActionConfirmationDialogService _deviceActionConfirmationDialogService;
        private readonly IAdvancedChangeConfigDialogService _advancedChangeConfigDialogService;
        private readonly IRandomDeviceInfoDialogService _randomDeviceInfoDialogService;
        private readonly IDeviceListService _deviceListService;
        private readonly IDeviceConfigService _deviceConfigService;
        private readonly IRandomDeviceService _randomDeviceService;
        private readonly ISimProfileService _simProfileService;
        private readonly IDeviceActionCoordinatorService _deviceActionCoordinatorService;
        private readonly IDeviceProcessStateService _deviceProcessStateService;
        private readonly IDeviceActionService _deviceActionService;
        private readonly IDeviceChangeService _deviceChangeService;
        private readonly ILocalizationService _localizationService;
        private readonly ISettingsService _settingsService;
        private readonly AppSettings _settings;
        private readonly ILogger<ChangeSingleDeviceViewModel> _logger;
        private readonly IUiDispatcherService _uiDispatcher;
        private readonly IPollingService _pollingService;
        private readonly SemaphoreSlim _deviceRefreshLock = new(1, 1);
        private readonly SemaphoreSlim _lifecycleLock = new(1, 1);
        private readonly object _pendingDeviceEditsLock = new();
        private readonly Dictionary<string, PendingDeviceEdit> _pendingDeviceEdits = new(StringComparer.OrdinalIgnoreCase);
        private readonly object _pendingProfileEditLock = new();
        private readonly Dictionary<string, PendingDeviceProfileEdit> _pendingProfileEdits = new(StringComparer.OrdinalIgnoreCase);
        private readonly object _activeActionOperationsLock = new();
        private readonly HashSet<Guid> _activeActionOperationIds = [];
        private TaskCompletionSource? _activeActionOperationsCompletion;
        private CancellationTokenSource? _pollCancellation;
        private CancellationTokenSource _actionLifetimeCancellation = new();
        private Task? _pollTask;
        private List<StoredDeviceConfig> _storedDevices = new();
        private List<CarrierProfile> _carrierProfiles = new();
        private readonly Dictionary<string, DeviceInfoApiDevice> _randomDeviceProfiles = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, SimProfile> _randomSimProfiles = new(StringComparer.OrdinalIgnoreCase);
        private DeviceChangeOptions _deviceChangeOptions = new();

        private DeviceRowViewModel? _selectedDevice;
        private bool _isRefreshingRows;
        private bool _isSynchronizingSelection;
        private bool _isApplyingDeviceConfig;
        private bool _isSynchronizingDeviceInfo;
        private bool _isUpdatingCarrierOptions;
        [ObservableProperty]
        private bool _isChangeSimEnabled = true;
        private bool _useIntegritySecurityPatch = true;
        [ObservableProperty]
        private bool _useDefaultChangeMode = true;
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
        [ObservableProperty]
        private string _deviceSearchText = string.Empty;

        public ChangeSingleDeviceViewModel(
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
            IPackageInstallService packageInstallService,
            IDeviceActionConfirmationDialogService deviceActionConfirmationDialogService,
            IAdvancedChangeConfigDialogService advancedChangeConfigDialogService,
            IRandomDeviceInfoDialogService randomDeviceInfoDialogService,
            IDeviceListService deviceListService,
            IDeviceConfigService deviceConfigService,
            IRandomDeviceService randomDeviceService,
            ISimProfileService simProfileService,
            IDeviceActionCoordinatorService deviceActionCoordinatorService,
            IDeviceProcessStateService deviceProcessStateService,
            IDeviceActionService deviceActionService,
            IDeviceChangeService deviceChangeService,
            ILocalizationService localizationService,
            ISettingsService settingsService,
            AppSettings settings,
            IUiDispatcherService uiDispatcher,
            IPollingService pollingService,
            ILogger<ChangeSingleDeviceViewModel> logger)
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
            _packageInstallService = packageInstallService;
            _deviceActionConfirmationDialogService = deviceActionConfirmationDialogService;
            _advancedChangeConfigDialogService = advancedChangeConfigDialogService;
            _randomDeviceInfoDialogService = randomDeviceInfoDialogService;
            _deviceListService = deviceListService;
            _deviceConfigService = deviceConfigService;
            _randomDeviceService = randomDeviceService;
            _simProfileService = simProfileService;
            _deviceActionCoordinatorService = deviceActionCoordinatorService;
            _deviceProcessStateService = deviceProcessStateService;
            _deviceActionService = deviceActionService;
            _deviceChangeService = deviceChangeService;
            _localizationService = localizationService;
            _settingsService = settingsService;
            _settings = settings;
            _deviceChangeOptions = DeviceChangeOptionsHelper.CreateNormalizedCopy(new DeviceChangeOptions());
            _useDefaultChangeMode = _deviceChangeOptions.UseDefaultMode;
            _uiDispatcher = uiDispatcher;
            _pollingService = pollingService;
            _logger = logger;
            Devices = new ObservableCollection<DeviceRowViewModel>();
            Countries = new ObservableCollection<CarrierCountryOption>();
            Carriers = new ObservableCollection<CarrierOption>();
            AndroidVersions = new ObservableCollection<string>();
            DeviceInfo = CreateDefaultDeviceInfo();
            DeviceInfo.PropertyChanged += OnDeviceInfoPropertyChanged;
            _deviceActionCoordinatorService.OperationStateChanged += OnDeviceActionStateChanged;
            _deviceProcessStateService.ProcessChanged += OnDeviceProcessChanged;

            Brands = DeviceProfileOptionsHelper.Brands;
            UpdateAndroidVersionOptions("Random", null);
            TypeOptions = ["sargo", "starlte", "tissot", "unknown"];
            NewDeviceCountText = string.Format(_localizationService.GetString("ChangeSingleDevice_NewDeviceCount"), 0);

            SelectedBrand = Brands.FirstOrDefault();
            SelectedAndroidVersion = AndroidVersions.FirstOrDefault();
            _selectedDeviceFilter = "All";
        }

        public ObservableCollection<DeviceRowViewModel> Devices { get; }
        public DeviceInfoFormViewModel DeviceInfo { get; }
        public IReadOnlyList<string> Brands { get; }
        public ObservableCollection<string> AndroidVersions { get; }
        public ObservableCollection<CarrierCountryOption> Countries { get; }
        public ObservableCollection<CarrierOption> Carriers { get; }
        public IReadOnlyList<string> TypeOptions { get; }
        public IReadOnlyDictionary<string, double> DeviceTableColumnRatios =>
            _settings.DeviceTableColumnRatios;
        public bool CanInteractWithSelectedDevice => SelectedDevice == null || !IsDeviceBusy(SelectedDevice);
        public bool IsSelectedDeviceActionBusy =>
            SelectedDevice != null && IsDeviceBusy(SelectedDevice);
        public DeviceActionKind? ActiveSelectedDeviceActionKind =>
            GetSelectedDeviceOperation()?.Kind;
        public DeviceActionKind? DisplayedSelectedDeviceActionKind =>
            GetSelectedDeviceOperation()?.Kind.ToLogicalActionKind();
        public bool HasExternalSelectedDeviceAction =>
            GetSelectedDeviceOperation() is { } operation && operation.Kind.IsBatchAction();
        public string ExternalSelectedDeviceActionText =>
            GetExternalSelectedDeviceActionText();
        public bool HasActiveSelectedDeviceActionButton =>
            GetSelectedDeviceActionButtonPosition().HasValue;
        public int ActiveSelectedDeviceActionButtonRow =>
            GetSelectedDeviceActionButtonPosition()?.Row ?? 0;
        public int ActiveSelectedDeviceActionButtonColumn =>
            GetSelectedDeviceActionButtonPosition()?.Column ?? 0;
        public bool IsSelectedDeviceActionRunning =>
            GetSelectedDeviceOperation()?.State == DeviceActionRuntimeState.Running;
        public bool IsSelectedDeviceActionStopping =>
            GetSelectedDeviceOperation()?.State == DeviceActionRuntimeState.Stopping;
        public bool CanEditSelectedDeviceConfiguration =>
            SelectedDevice != null && !IsDeviceBusy(SelectedDevice);
        public bool CanStopSelectedDeviceAction =>
            GetSelectedDeviceOperation() is
            {
                State: DeviceActionRuntimeState.Running,
                CanCancel: true
            } operation
            && !operation.Kind.IsBatchAction();
        public string SelectedDeviceActionStopText =>
            _localizationService.GetString(IsSelectedDeviceActionStopping
                ? "ChangeSingleDevice_StoppingAction"
                : "ChangeSingleDevice_StopAction");

        public async Task InitializeAsync(CancellationToken cancellationToken)
        {
            await _lifecycleLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                ObjectDisposedException.ThrowIf(_isDisposed, this);
                if (_actionLifetimeCancellation.IsCancellationRequested)
                {
                    _actionLifetimeCancellation.Dispose();
                    _actionLifetimeCancellation = new CancellationTokenSource();
                }

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
            _actionLifetimeCancellation.Cancel();
            Task activeActions;
            lock (_activeActionOperationsLock)
                activeActions = _activeActionOperationsCompletion?.Task ?? Task.CompletedTask;
            await activeActions.ConfigureAwait(false);
            await SuspendAsync().ConfigureAwait(false);
        }

        public async Task SuspendAsync()
        {
            await _lifecycleLock.WaitAsync().ConfigureAwait(false);
            try
            {
                await StopPollingAsync().ConfigureAwait(false);
                await FlushPendingDeviceEditsAsync().ConfigureAwait(false);
                await FlushPendingDeviceProfileAsync().ConfigureAwait(false);
            }
            finally
            {
                _lifecycleLock.Release();
            }
        }

        private async Task StopPollingAsync()
        {
            CancellationTokenSource? cancellation = _pollCancellation;
            Task? polling = _pollTask;
            cancellation?.Cancel();
            try
            {
                if (polling != null)
                    await polling.ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellation?.IsCancellationRequested == true)
            {
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Single Device polling failed while suspending the view.");
            }
            finally
            {
                _pollTask = null;
                _pollCancellation = null;
                cancellation?.Dispose();
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

        partial void OnDeviceSearchTextChanged(string value)
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

        partial void OnUseDefaultChangeModeChanged(bool value)
        {
            _deviceChangeOptions.UseDefaultMode = value;
            OpenAdvancedChangeConfigCommand.NotifyCanExecuteChanged();

            if (!_isApplyingDeviceConfig)
                QueueSelectedDeviceProfileSave();
        }

        public void Dispose()
        {
            if (_isDisposed)
                return;

            _actionLifetimeCancellation.Cancel();
            FlushPendingDeviceEditsAsync().GetAwaiter().GetResult();
            FlushPendingDeviceProfileAsync().GetAwaiter().GetResult();
            _isDisposed = true;
            _pollCancellation?.Cancel();

            foreach (var device in _allDeviceRows)
                device.PropertyChanged -= OnDeviceRowPropertyChanged;

            DeviceInfo.PropertyChanged -= OnDeviceInfoPropertyChanged;
            _deviceActionCoordinatorService.OperationStateChanged -= OnDeviceActionStateChanged;
            _deviceProcessStateService.ProcessChanged -= OnDeviceProcessChanged;
            _pollCancellation?.Dispose();
            _pollCancellation = null;
            _actionLifetimeCancellation.Dispose();
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
            _deviceProcessStateService.SetProcess(device.Serial, message, resourceKey);
            _logger.LogInformation("Device {Serial} action: {Message}", device.Serial, message);
        }

        private async Task ShowToolbarLogAsync(string resourceKey, CancellationToken cancellationToken)
        {
            _isShowingToolbarLog = true;
            var message = GetLogText(resourceKey);
            NewDeviceCountText = message;
            _logger.LogInformation("Devices toolbar action: {Message}", message);

            try
            {
                await Task.Delay(1000, cancellationToken).ConfigureAwait(true);
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

        private bool CanExecuteSelectedDeviceAction()
        {
            return CanInteractWithSelectedDevice;
        }

        private bool CanExecuteDeviceAction(DeviceRowViewModel? device)
        {
            DeviceRowViewModel? targetDevice = device ?? SelectedDevice;
            return targetDevice == null || !IsDeviceBusy(targetDevice);
        }

        private bool IsDeviceBusy(DeviceRowViewModel device)
        {
            return _deviceActionCoordinatorService.IsBusy(device.Serial);
        }

        private async Task<IDeviceActionOperation?> StartOnlineDeviceActionAsync(
            DeviceRowViewModel? device,
            DeviceActionKind kind,
            bool canCancel = true)
        {
            if (device == null)
            {
                await ShowToolbarLogAsync("Log_SelectDeviceFirst", CancellationToken.None)
                    .ConfigureAwait(true);
                return null;
            }

            if (device.ConnectionStatus != AdbDeviceStatus.Online)
            {
                SetDeviceLog(device, "Log_DeviceMustBeOnline");
                return null;
            }

            IDeviceActionOperation? operation = TryStartDeviceAction(device, kind, canCancel);
            if (operation == null)
                return null;

            try
            {
                if (await _deviceListService
                        .IsDeviceOnlineAsync(device.Serial, operation.CancellationToken)
                        .ConfigureAwait(true))
                {
                    return operation;
                }

                SetDeviceLog(device, "Log_DeviceMustBeOnline");
            }
            catch (OperationCanceledException)
            {
                SetOperationCancellationLog(device, operation, GetDeviceActionCanceledLogKey(kind));
                operation.Dispose();
                return null;
            }
            catch
            {
                operation.Dispose();
                throw;
            }

            operation.Dispose();
            return null;
        }

        private string GetDeviceActionCanceledLogKey(DeviceActionKind kind)
        {
            return kind switch
            {
                DeviceActionKind.ChangeDevice => "Log_ChangeDeviceCanceled",
                DeviceActionKind.RandomChangeAndWipe => "Log_ChangeDeviceCanceled",
                DeviceActionKind.ChangeWithoutWipe => "Log_ChangeWithoutWipeCanceled",
                DeviceActionKind.Wipe => "Log_WipeWithoutChangeCanceled",
                DeviceActionKind.RandomDevice => "Log_RandomDeviceCanceled",
                DeviceActionKind.RandomSim => "Log_RandomSimCanceled",
                DeviceActionKind.ChangeSim => "Log_ChangeSimCanceled",
                DeviceActionKind.ChangeLocation => "Log_ChangeLocationCanceled",
                DeviceActionKind.ChangeTimezone => "Log_ChangeTimezoneCanceled",
                DeviceActionKind.InstallPackages => "Log_InstallPackageCanceled",
                DeviceActionKind.DeleteDevice => "Log_DeleteDeviceCanceled",
                DeviceActionKind.AdvancedChangeConfig => "Log_AdvancedChangeConfigCanceled",
                DeviceActionKind.UpdateIntegrity => "Log_UpdateIntegrityCanceled",
                DeviceActionKind.FakeProxy => "Log_FakeProxyCanceled",
                DeviceActionKind.StopFakeProxy => "Log_StopFakeProxyCanceled",
                DeviceActionKind.ViewRandomDeviceInfo => "Log_ViewRandomDeviceInfoCanceled",
                _ => "Log_Ready"
            };
        }

        private void SetOperationCancellationLog(
            DeviceRowViewModel device,
            IDeviceActionOperation operation,
            string userStopLogKey)
        {
            if (device.ConnectionStatus != AdbDeviceStatus.Online)
            {
                SetDeviceLog(device, "Log_DeviceMustBeOnline");
                return;
            }

            SetDeviceLog(
                device,
                operation.CancellationReason == DeviceActionCancellationReason.UserStop
                    ? userStopLogKey
                    : "Log_Ready");
        }

        private void SetDialogDismissalLog(
            DeviceRowViewModel device,
            IDeviceActionOperation operation,
            string canceledLogKey)
        {
            if (operation.CancellationReason == DeviceActionCancellationReason.None
                && device.ConnectionStatus == AdbDeviceStatus.Online)
            {
                SetDeviceLog(device, canceledLogKey);
                return;
            }

            SetOperationCancellationLog(device, operation, canceledLogKey);
        }

        private IDeviceActionOperation? TryStartDeviceAction(
            DeviceRowViewModel device,
            DeviceActionKind kind,
            bool canCancel = true)
        {
            IDeviceActionOperation? operation = _deviceActionCoordinatorService.TryStart(
                device.Serial,
                kind,
                canCancel,
                _actionLifetimeCancellation.Token);
            if (operation == null)
                return null;

            lock (_activeActionOperationsLock)
            {
                if (_activeActionOperationIds.Count == 0)
                {
                    _activeActionOperationsCompletion = new TaskCompletionSource(
                        TaskCreationOptions.RunContinuationsAsynchronously);
                }

                _activeActionOperationIds.Add(operation.OperationId);
            }

            return operation;
        }

        private DeviceActionOperationSnapshot? GetSelectedDeviceOperation()
        {
            return SelectedDevice == null
                ? null
                : _deviceActionCoordinatorService.GetOperation(SelectedDevice.Serial);
        }

        private string GetExternalSelectedDeviceActionText()
        {
            DeviceActionOperationSnapshot? operation = GetSelectedDeviceOperation();
            if (operation == null || !operation.Kind.IsBatchAction())
                return string.Empty;

            string format = GetLogText(operation.State == DeviceActionRuntimeState.Stopping
                ? "ChangeSingleDevice_ExternalActionStoppingFormat"
                : "ChangeSingleDevice_ExternalActionRunningFormat");
            return string.Format(
                format,
                GetLogText(operation.Kind.GetDisplayResourceKey()));
        }

        private (int Row, int Column)? GetSelectedDeviceActionButtonPosition()
        {
            return ActiveSelectedDeviceActionKind switch
            {
                DeviceActionKind.RandomDevice => (0, 0),
                DeviceActionKind.ChangeDevice => (0, 1),
                DeviceActionKind.Wipe => (1, 0),
                DeviceActionKind.ChangeWithoutWipe => (1, 1),
                DeviceActionKind.RandomSim => (2, 0),
                DeviceActionKind.ChangeSim => (2, 1),
                DeviceActionKind.RandomChangeAndWipe => (3, 0),
                DeviceActionKind.InstallPackages => (3, 1),
                DeviceActionKind.ChangeLocation => (4, 0),
                DeviceActionKind.ChangeTimezone => (4, 1),
                DeviceActionKind.FakeProxy => (5, 0),
                DeviceActionKind.StopFakeProxy => (5, 1),
                DeviceActionKind.UpdateIntegrity => (6, 0),
                _ => null
            };
        }

        private void OnDeviceActionStateChanged(DeviceActionOperationSnapshot snapshot)
        {
            if (_isDisposed)
                return;

            if (snapshot.State == DeviceActionRuntimeState.Idle)
            {
                TaskCompletionSource? completion = null;
                lock (_activeActionOperationsLock)
                {
                    if (_activeActionOperationIds.Remove(snapshot.OperationId)
                        && _activeActionOperationIds.Count == 0)
                    {
                        completion = _activeActionOperationsCompletion;
                        _activeActionOperationsCompletion = null;
                    }
                }

                completion?.TrySetResult();
            }

            void ApplyBusyState()
            {
                bool isBusy = snapshot.State != DeviceActionRuntimeState.Idle;
                foreach (DeviceRowViewModel device in _allDeviceRows.Where(device => SerialEquals(device.Serial, snapshot.Serial)))
                    device.IsActionBusy = isBusy;

                bool selectedDeviceChangedBusy = SelectedDevice != null
                    && SerialEquals(SelectedDevice.Serial, snapshot.Serial);
                if (selectedDeviceChangedBusy)
                    NotifyDeviceInteractionChanged();
                else
                    DeleteDeviceCommand.NotifyCanExecuteChanged();
            }

            if (_uiDispatcher.CheckAccess())
            {
                ApplyBusyState();
                return;
            }

            TrackSilentSave(
                _uiDispatcher.InvokeAsync(ApplyBusyState),
                "Failed to update device action busy state.");
        }

        private void OnDeviceProcessChanged(DeviceProcessSnapshot snapshot)
        {
            if (_isDisposed)
                return;

            void ApplyProcessState()
            {
                foreach (DeviceRowViewModel device in _allDeviceRows
                             .Where(device => SerialEquals(device.Serial, snapshot.Serial)))
                {
                    device.RestoreProcess(snapshot.Message, snapshot.State);
                }
            }

            if (_uiDispatcher.CheckAccess())
            {
                ApplyProcessState();
                return;
            }

            TrackSilentSave(
                _uiDispatcher.InvokeAsync(ApplyProcessState),
                "Failed to update shared device process state.");
        }

        private void NotifyDeviceInteractionChanged()
        {
            OnPropertyChanged(nameof(CanInteractWithSelectedDevice));
            OnPropertyChanged(nameof(IsSelectedDeviceActionBusy));
            OnPropertyChanged(nameof(ActiveSelectedDeviceActionKind));
            OnPropertyChanged(nameof(DisplayedSelectedDeviceActionKind));
            OnPropertyChanged(nameof(HasExternalSelectedDeviceAction));
            OnPropertyChanged(nameof(ExternalSelectedDeviceActionText));
            OnPropertyChanged(nameof(HasActiveSelectedDeviceActionButton));
            OnPropertyChanged(nameof(ActiveSelectedDeviceActionButtonRow));
            OnPropertyChanged(nameof(ActiveSelectedDeviceActionButtonColumn));
            OnPropertyChanged(nameof(IsSelectedDeviceActionRunning));
            OnPropertyChanged(nameof(IsSelectedDeviceActionStopping));
            OnPropertyChanged(nameof(CanEditSelectedDeviceConfiguration));
            OnPropertyChanged(nameof(CanStopSelectedDeviceAction));
            OnPropertyChanged(nameof(SelectedDeviceActionStopText));
            DeleteDeviceCommand.NotifyCanExecuteChanged();
            RandomDeviceCommand.NotifyCanExecuteChanged();
            ChangeDeviceCommand.NotifyCanExecuteChanged();
            ChangeWithoutWipeCommand.NotifyCanExecuteChanged();
            WipeWithoutChangeCommand.NotifyCanExecuteChanged();
            OpenAdvancedChangeConfigCommand.NotifyCanExecuteChanged();
            RandomChangeAndWipeDeviceCommand.NotifyCanExecuteChanged();
            RandomSimCommand.NotifyCanExecuteChanged();
            ChangeSimCommand.NotifyCanExecuteChanged();
            ChangeLocationCommand.NotifyCanExecuteChanged();
            ChangeTimezoneCommand.NotifyCanExecuteChanged();
            ViewRandomDeviceInfoCommand.NotifyCanExecuteChanged();
            UpdateIntegrityCommand.NotifyCanExecuteChanged();
            InstallApkCommand.NotifyCanExecuteChanged();
            FakeProxyCommand.NotifyCanExecuteChanged();
            StopFakeProxyCommand.NotifyCanExecuteChanged();
            StopSelectedDeviceActionCommand.NotifyCanExecuteChanged();
        }

        [RelayCommand(CanExecute = nameof(CanStopSelectedDeviceActionCommandCanExecute))]
        private void StopSelectedDeviceAction()
        {
            DeviceRowViewModel? device = SelectedDevice;
            if (device == null || !CanStopSelectedDeviceAction)
                return;

            _deviceActionCoordinatorService.TryRequestCancellation(device.Serial);
        }

        private bool CanStopSelectedDeviceActionCommandCanExecute()
        {
            return CanStopSelectedDeviceAction;
        }

        private bool CanAddNewDevices()
        {
            return !IsLoadingDevices;
        }

        [RelayCommand(CanExecute = nameof(CanAddNewDevices))]
        private async Task AddNewDevicesAsync(CancellationToken cancellationToken)
        {
            IsLoadingDevices = true;
            await ShowToolbarLogAsync("Log_AddDevicesOpening", cancellationToken).ConfigureAwait(true);

            try
            {
                var selectedDevices = await _addDevicesDialogService.ShowAddDevicesAsync(cancellationToken).ConfigureAwait(true);
                if (selectedDevices.Count == 0)
                {
                    await ShowToolbarLogAsync("Log_AddDevicesCanceled", cancellationToken).ConfigureAwait(true);
                    return;
                }

                await ShowToolbarLogAsync("Log_SavingDevices", cancellationToken).ConfigureAwait(true);

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

                await ShowToolbarLogAsync("Log_AddDevicesSuccess", cancellationToken).ConfigureAwait(true);
            }
            catch (OperationCanceledException)
            {
                await ShowToolbarLogAsync("Log_AddDevicesCanceled", CancellationToken.None).ConfigureAwait(true);
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Failed to add new devices.");
                await ShowToolbarLogAsync("Log_AddDevicesFailed", CancellationToken.None).ConfigureAwait(true);
            }
            finally
            {
                IsLoadingDevices = false;
            }
        }

        [RelayCommand]
        private void ToggleDeviceSelection(DeviceRowViewModel? device)
        {
            if (device == null)
                return;

            SelectSingleDevice(ReferenceEquals(SelectedDevice, device) ? null : device);
        }

        [RelayCommand(CanExecute = nameof(CanExecuteDeviceAction), AllowConcurrentExecutions = true)]
        private async Task DeleteDeviceAsync(DeviceRowViewModel? device)
        {
            if (device == null)
            {
                await ShowToolbarLogAsync("Log_SelectDeviceFirst", CancellationToken.None)
                    .ConfigureAwait(true);
                return;
            }

            SelectSingleDevice(device);

            string serial = device.Serial;
            string name = device.Name;
            IDeviceActionOperation? operation = TryStartDeviceAction(
                device,
                DeviceActionKind.DeleteDevice,
                canCancel: false);
            if (operation == null)
                return;

            using (operation)
            {
                CancellationToken cancellationToken = operation.CancellationToken;
                try
                {
                    bool confirmed = await _deviceActionConfirmationDialogService
                        .ConfirmDeleteDeviceAsync(name, serial, cancellationToken)
                        .ConfigureAwait(true);

                    if (!confirmed)
                    {
                        SetDialogDismissalLog(device, operation, "Log_DeleteDeviceCanceled");
                        return;
                    }

                    SetDeviceLog(device, "Log_DeletingDevice");

                    await _deviceRefreshLock.WaitAsync(cancellationToken).ConfigureAwait(true);
                    try
                    {
                        var deleteResult = await _deviceListService
                            .DeleteSavedDeviceAsync(serial, cancellationToken)
                            .ConfigureAwait(true);
                        if (!deleteResult.Removed)
                        {
                            SetDeviceLog(device, "Log_DeleteDeviceFailed");
                            return;
                        }

                        _randomDeviceProfiles.Remove(serial);
                        _randomSimProfiles.Remove(serial);
                        ApplyDeviceListSnapshot(deleteResult.Snapshot);
                    }
                    finally
                    {
                        _deviceRefreshLock.Release();
                    }

                    await ShowToolbarLogAsync("Log_DeleteDeviceSuccess", cancellationToken)
                        .ConfigureAwait(true);
                }
                catch (OperationCanceledException)
                {
                    SetOperationCancellationLog(device, operation, "Log_DeleteDeviceCanceled");
                }
                catch (Exception exception)
                {
                    _logger.LogError(exception, "Failed to delete device {Serial}.", serial);
                    SetDeviceLog(device, "Log_DeleteDeviceFailed");
                }
            }
        }

        [RelayCommand(AllowConcurrentExecutions = true)]
        private async Task RebootDeviceAsync(DeviceRowViewModel? device)
        {
            CancellationToken cancellationToken = CancellationToken.None;
            device = await GetOnlineDeviceAsync(device, cancellationToken).ConfigureAwait(true);
            if (device == null)
                return;

            SetDeviceLog(device, "Log_RebootingDevice");

            try
            {
                await _deviceActionService.RebootAsync(device.Serial, cancellationToken).ConfigureAwait(true);
                SetDeviceLog(device, "Log_RebootDeviceSuccess");
            }
            catch (OperationCanceledException)
            {
                SetDeviceLog(device, "Log_Ready");
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Failed to reboot device {Serial}.", device.Serial);
                SetDeviceLog(device, "Log_RebootDeviceFailed");
            }
        }

        [RelayCommand]
        private async Task CopySerialAsync(DeviceRowViewModel? device, CancellationToken cancellationToken)
        {
            DeviceRowViewModel? targetDevice = device ?? SelectedDevice;
            if (targetDevice == null || string.IsNullOrWhiteSpace(targetDevice.Serial))
                return;

            bool success = false;
            await RunOnUiContextAsync(() =>
            {
                try
                {
                    System.Windows.Clipboard.SetText(targetDevice.Serial);
                    success = true;
                }
                catch (Exception exception)
                {
                    _logger.LogError(exception, "Failed to copy serial for device {Serial}.", targetDevice.Serial);
                }
            }).ConfigureAwait(true);

            if (success)
            {
                SetDeviceLog(targetDevice, "Log_CopySerialSuccess");
            }
            else
            {
                SetDeviceLog(targetDevice, "Log_CopySerialFailed");
            }
        }

        [RelayCommand(AllowConcurrentExecutions = true)]
        private async Task RefreshGooglePackageStateAsync(DeviceRowViewModel? device)
        {
            CancellationToken cancellationToken = CancellationToken.None;
            device = await GetOnlineDeviceAsync(device, cancellationToken).ConfigureAwait(true);
            if (device == null)
                return;

            try
            {
                GooglePackageState state = await _deviceActionService
                    .GetGooglePackageStateAsync(device.Serial, cancellationToken)
                    .ConfigureAwait(true);
                ApplyGooglePackageState(device, state);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Failed to read Google package state for device {Serial}.", device.Serial);
                SetDeviceLog(device, "Log_GooglePackageStateFailed");
            }
        }

        [RelayCommand(AllowConcurrentExecutions = true)]
        private async Task RefreshContextMenuStateAsync(DeviceRowViewModel? device)
        {
            CancellationToken cancellationToken = CancellationToken.None;
            device = await GetOnlineDeviceAsync(device, cancellationToken).ConfigureAwait(true);
            if (device == null)
                return;

            device.IsContextMenuStateLoading = true;
            try
            {
                Task<GooglePackageState> googlePackageStateTask = _deviceActionService
                    .GetGooglePackageStateAsync(device.Serial, cancellationToken);
                Task<bool> wifiStateTask = _deviceActionService
                    .GetWifiEnabledAsync(device.Serial, cancellationToken);

                try
                {
                    await Task.WhenAll(googlePackageStateTask, wifiStateTask).ConfigureAwait(true);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (Exception exception)
                {
                    _logger.LogError(exception, "Failed to read context menu state for device {Serial}.", device.Serial);
                }

                if (googlePackageStateTask.IsCompletedSuccessfully)
                    ApplyGooglePackageState(device, googlePackageStateTask.Result);

                if (wifiStateTask.IsCompletedSuccessfully)
                    device.IsWifiEnabled = wifiStateTask.Result;

                if (!googlePackageStateTask.IsCompletedSuccessfully
                    || !wifiStateTask.IsCompletedSuccessfully)
                {
                    SetDeviceLog(device, "Log_ContextMenuStateFailed");
                }
            }
            finally
            {
                device.IsContextMenuStateLoading = false;
            }
        }

        [RelayCommand(AllowConcurrentExecutions = true)]
        private Task ToggleGmsAsync(DeviceRowViewModel? device)
        {
            return ToggleGooglePackageAsync(device, isGms: true, CancellationToken.None);
        }

        [RelayCommand(AllowConcurrentExecutions = true)]
        private Task TogglePlayStoreAsync(DeviceRowViewModel? device)
        {
            return ToggleGooglePackageAsync(device, isGms: false, CancellationToken.None);
        }

        private async Task ToggleGooglePackageAsync(
            DeviceRowViewModel? device,
            bool isGms,
            CancellationToken cancellationToken)
        {
            device = await GetOnlineDeviceAsync(device, cancellationToken).ConfigureAwait(true);
            if (device == null)
                return;

            try
            {
                GooglePackageState state = await _deviceActionService
                    .GetGooglePackageStateAsync(device.Serial, cancellationToken)
                    .ConfigureAwait(true);
                ApplyGooglePackageState(device, state);

                bool enabled = isGms ? state.IsGmsDisabled : state.IsPlayStoreDisabled;
                if (isGms)
                {
                    await _deviceActionService
                        .SetGmsEnabledAsync(device.Serial, enabled, cancellationToken)
                        .ConfigureAwait(true);
                    device.IsGmsDisabled = !enabled;
                }
                else
                {
                    await _deviceActionService
                        .SetPlayStoreEnabledAsync(device.Serial, enabled, cancellationToken)
                        .ConfigureAwait(true);
                    device.IsPlayStoreDisabled = !enabled;
                }

                string successLog = (isGms, enabled) switch
                {
                    (true, true) => "Log_GmsEnabled",
                    (true, false) => "Log_GmsDisabled",
                    (false, true) => "Log_PlayStoreEnabled",
                    _ => "Log_PlayStoreDisabled"
                };
                SetDeviceLog(device, successLog);
            }
            catch (OperationCanceledException)
            {
                SetDeviceLog(device, "Log_Ready");
            }
            catch (Exception exception)
            {
                _logger.LogError(
                    exception,
                    "Failed to toggle {Package} for device {Serial}.",
                    isGms ? "GMS" : "Play Store",
                    device.Serial);
                SetDeviceLog(
                    device,
                    isGms ? "Log_GmsToggleFailed" : "Log_PlayStoreToggleFailed");
            }
        }

        private static void ApplyGooglePackageState(
            DeviceRowViewModel device,
            GooglePackageState state)
        {
            device.IsGmsDisabled = state.IsGmsDisabled;
            device.IsPlayStoreDisabled = state.IsPlayStoreDisabled;
        }

        [RelayCommand(AllowConcurrentExecutions = true)]
        private async Task ToggleWifiAsync(DeviceRowViewModel? device)
        {
            CancellationToken cancellationToken = CancellationToken.None;
            device = await GetOnlineDeviceAsync(device, cancellationToken).ConfigureAwait(true);
            if (device == null)
                return;

            try
            {
                bool isWifiEnabled = await _deviceActionService
                    .GetWifiEnabledAsync(device.Serial, cancellationToken)
                    .ConfigureAwait(true);
                device.IsWifiEnabled = isWifiEnabled;

                bool enabled = !isWifiEnabled;
                await _deviceActionService
                    .SetWifiEnabledAsync(device.Serial, enabled, cancellationToken)
                    .ConfigureAwait(true);
                device.IsWifiEnabled = enabled;

                SetDeviceLog(device, enabled ? "Log_WifiEnabled" : "Log_WifiDisabled");
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Failed to toggle Wi-Fi for device {Serial}.", device.Serial);
                SetDeviceLog(device, "Log_WifiToggleFailed");
            }
        }

        [RelayCommand(AllowConcurrentExecutions = true)]
        private async Task ViewDeviceInfoAsync(DeviceRowViewModel? device)
        {
            CancellationToken cancellationToken = CancellationToken.None;
            device = await GetOnlineDeviceAsync(device, cancellationToken).ConfigureAwait(true);
            if (device == null)
                return;
        }

        [RelayCommand(CanExecute = nameof(CanExecuteSelectedDeviceAction), AllowConcurrentExecutions = true)]
        private async Task RandomDeviceAsync()
        {
            DeviceRowViewModel? selectedDevice = SelectedDevice;
            RandomDeviceRequest request = CreateCurrentRandomDeviceRequest();
            IDeviceActionOperation? operation = await StartOnlineDeviceActionAsync(
                    selectedDevice,
                    DeviceActionKind.RandomDevice)
                .ConfigureAwait(true);
            if (operation == null)
                return;

            using (operation)
            {
                DeviceRowViewModel device = selectedDevice!;
                CancellationToken cancellationToken = operation.CancellationToken;

                try
                {
                    SetDeviceLog(device, "Log_RandomDevice");
                    var randomResult = await _randomDeviceService
                        .CreateRandomProfileAsync(request, cancellationToken)
                        .ConfigureAwait(true);

                    if (randomResult.Status == RandomDeviceStatus.LoginRequired)
                    {
                        SetDeviceLog(device, "Log_RandomDeviceLoginRequired");
                        return;
                    }

                    if (randomResult.Status == RandomDeviceStatus.Failed || randomResult.Profile == null)
                    {
                        SetDeviceLog(device, "Log_RandomDeviceFailed");
                        return;
                    }

                    ApplyRandomDeviceInfo(device.Serial, randomResult.Profile.Clone());
                    SetDeviceLog(device, "Log_RandomDeviceSuccess");
                }
                catch (OperationCanceledException)
                {
                    SetOperationCancellationLog(device, operation, "Log_RandomDeviceCanceled");
                }
                catch (Exception exception)
                {
                    _logger.LogError(exception, "Unexpected failure while randomizing device info.");
                    SetDeviceLog(device, "Log_RandomDeviceFailed");
                }
            }
        }

        [RelayCommand(CanExecute = nameof(CanExecuteSelectedDeviceAction), AllowConcurrentExecutions = true)]
        private async Task ChangeDeviceAsync()
        {
            DeviceRowViewModel? selectedDevice = SelectedDevice;
            DeviceInfoApiDevice? profile = CreateRandomDeviceProfileSnapshot(selectedDevice);
            if (profile != null)
                CopyFormValuesToProfile(profile);

            if (selectedDevice != null && profile == null)
            {
                SetDeviceLog(selectedDevice, "Log_RandomDeviceRequired");
                return;
            }

            DeviceChangeOptions changeOptions = CreateCurrentChangeOptions();
            bool changeSimEnabled = IsChangeSimEnabled;
            IDeviceActionOperation? operation = await StartOnlineDeviceActionAsync(
                    selectedDevice,
                    DeviceActionKind.ChangeDevice)
                .ConfigureAwait(true);
            if (operation == null)
                return;

            using (operation)
            {
                DeviceRowViewModel device = selectedDevice!;
                CancellationToken cancellationToken = operation.CancellationToken;

                SetDeviceLog(device, "Log_ChangeDevice");

                try
                {
                    bool confirmed = await _deviceActionConfirmationDialogService
                        .ConfirmChangeAndWipeAsync(
                            device.Name,
                            device.Serial,
                            changeOptions,
                            cancellationToken)
                        .ConfigureAwait(true);
                    if (!confirmed)
                    {
                        SetDialogDismissalLog(device, operation, "Log_ChangeDeviceCanceled");
                        return;
                    }

                    IProgress<DeviceChangeStage> progress = CreateDeviceChangeProgress(
                        device,
                        "Log_ChangeDevice",
                        "Log_ChangeDeviceSuccess");
                    await _deviceChangeService
                        .ChangeAsync(
                            device.Serial,
                            profile!,
                            changeSimEnabled,
                            changeOptions,
                            progress,
                            cancellationToken)
                        .ConfigureAwait(true);

                    SetDeviceLog(device, "Log_ChangeDeviceSuccess");
                }
                catch (OperationCanceledException)
                {
                    SetOperationCancellationLog(device, operation, "Log_ChangeDeviceCanceled");
                }
                catch (Exception exception)
                {
                    _logger.LogError(exception, "Failed to change device {Serial}.", device.Serial);
                    SetDeviceLog(device, "Log_ChangeDeviceFailed");
                }
            }
        }

        [RelayCommand(CanExecute = nameof(CanExecuteSelectedDeviceAction), AllowConcurrentExecutions = true)]
        private async Task ChangeWithoutWipeAsync()
        {
            DeviceRowViewModel? selectedDevice = SelectedDevice;
            DeviceInfoApiDevice? profile = CreateRandomDeviceProfileSnapshot(selectedDevice);
            if (profile != null)
                CopyFormValuesToProfile(profile);

            if (selectedDevice != null && profile == null)
            {
                SetDeviceLog(selectedDevice, "Log_RandomDeviceRequired");
                return;
            }

            DeviceChangeOptions changeOptions = CreateCurrentChangeOptions();
            bool changeSimEnabled = IsChangeSimEnabled;
            IDeviceActionOperation? operation = await StartOnlineDeviceActionAsync(
                    selectedDevice,
                    DeviceActionKind.ChangeWithoutWipe)
                .ConfigureAwait(true);
            if (operation == null)
                return;

            using (operation)
            {
                DeviceRowViewModel device = selectedDevice!;
                CancellationToken cancellationToken = operation.CancellationToken;

                try
                {
                    bool confirmed = await _deviceActionConfirmationDialogService
                        .ConfirmChangeWithoutWipeAsync(device.Name, device.Serial, cancellationToken)
                        .ConfigureAwait(true);
                    if (!confirmed)
                    {
                        SetDialogDismissalLog(device, operation, "Log_ChangeWithoutWipeCanceled");
                        return;
                    }

                    SetDeviceLog(device, "Log_ChangeWithoutWipe");
                    IProgress<DeviceChangeStage> progress = CreateDeviceChangeProgress(
                        device,
                        "Log_ChangeWithoutWipe",
                        "Log_ChangeWithoutWipeSuccess");
                    await _deviceChangeService
                        .ChangeWithoutWipeAsync(
                            device.Serial,
                            profile!,
                            changeSimEnabled,
                            changeOptions,
                            progress,
                            cancellationToken)
                        .ConfigureAwait(true);
                    SetDeviceLog(device, "Log_ChangeWithoutWipeSuccess");
                }
                catch (OperationCanceledException)
                {
                    SetOperationCancellationLog(device, operation, "Log_ChangeWithoutWipeCanceled");
                }
                catch (Exception exception)
                {
                    _logger.LogError(exception, "Failed to change device {Serial} without wiping data.", device.Serial);
                    SetDeviceLog(device, "Log_ChangeWithoutWipeFailed");
                }
            }
        }

        [RelayCommand(CanExecute = nameof(CanExecuteSelectedDeviceAction), AllowConcurrentExecutions = true)]
        private async Task WipeWithoutChangeAsync()
        {
            DeviceRowViewModel? selectedDevice = SelectedDevice;
            DeviceChangeOptions changeOptions = CreateCurrentChangeOptions();
            IDeviceActionOperation? operation = await StartOnlineDeviceActionAsync(
                    selectedDevice,
                    DeviceActionKind.Wipe)
                .ConfigureAwait(true);
            if (operation == null)
                return;

            using (operation)
            {
                DeviceRowViewModel device = selectedDevice!;
                CancellationToken cancellationToken = operation.CancellationToken;

                try
                {
                    bool confirmed = await _deviceActionConfirmationDialogService
                        .ConfirmWipeWithoutChangeAsync(device.Name, device.Serial, cancellationToken)
                        .ConfigureAwait(true);
                    if (!confirmed)
                    {
                        SetDialogDismissalLog(device, operation, "Log_WipeWithoutChangeCanceled");
                        return;
                    }

                    SetDeviceLog(device, "Log_WipeWithoutChange");
                    IProgress<DeviceChangeStage> progress = CreateDeviceChangeProgress(
                        device,
                        "Log_WipeWithoutChange",
                        "Log_WipeWithoutChangeSuccess");
                    await _deviceChangeService
                        .WipeWithoutChangeAsync(
                            device.Serial,
                            changeOptions,
                            progress,
                            cancellationToken)
                        .ConfigureAwait(true);
                    SetDeviceLog(device, "Log_WipeWithoutChangeSuccess");
                }
                catch (OperationCanceledException)
                {
                    SetOperationCancellationLog(device, operation, "Log_WipeWithoutChangeCanceled");
                }
                catch (Exception exception)
                {
                    _logger.LogError(exception, "Failed to wipe device {Serial} without changing identity.", device.Serial);
                    SetDeviceLog(device, "Log_WipeWithoutChangeFailed");
                }
            }
        }

        private bool CanOpenAdvancedChangeConfig()
        {
            return !UseDefaultChangeMode && CanExecuteSelectedDeviceAction();
        }

        [RelayCommand(CanExecute = nameof(CanOpenAdvancedChangeConfig), AllowConcurrentExecutions = true)]
        private async Task OpenAdvancedChangeConfigAsync()
        {
            DeviceRowViewModel? selectedDevice = SelectedDevice;
            bool useDefaultChangeMode = UseDefaultChangeMode;
            DeviceProfileConfig profileSnapshot = CreateDeviceProfileConfig();
            if (useDefaultChangeMode)
                return;

            IDeviceActionOperation? operation = await StartOnlineDeviceActionAsync(
                    selectedDevice,
                    DeviceActionKind.AdvancedChangeConfig,
                    canCancel: false)
                .ConfigureAwait(true);
            if (operation == null)
                return;

            using (operation)
            {
                DeviceRowViewModel device = selectedDevice!;
                CancellationToken cancellationToken = operation.CancellationToken;
                DeviceChangeOptions optionsSnapshot = DeviceChangeOptionsHelper.CreateNormalizedCopy(
                    profileSnapshot.ChangeOptions,
                    useDefaultMode: false);
                SetDeviceLog(device, "Log_OpeningDialog");
                try
                {
                    AdvancedChangeConfigDialogResult? result = await _advancedChangeConfigDialogService
                        .ShowAdvancedChangeConfigAsync(
                            device.Serial,
                            optionsSnapshot,
                            profileSnapshot.UseIntegritySecurityPatch,
                            cancellationToken)
                        .ConfigureAwait(true);
                    if (result == null)
                    {
                        SetDeviceLog(device, "Log_Ready");
                        return;
                    }

                    profileSnapshot.ChangeOptions = DeviceChangeOptionsHelper.CreateNormalizedCopy(
                        result.Options,
                        useDefaultMode: false);
                    profileSnapshot.UseIntegritySecurityPatch = result.UseIntegritySecurityPatch;
                    await SaveDeviceProfileAsync(device.Serial, profileSnapshot, cancellationToken)
                        .ConfigureAwait(true);

                    if (SelectedDevice != null && SerialEquals(SelectedDevice.Serial, device.Serial))
                        ApplyStoredDeviceConfig(SelectedDevice);

                    SetDeviceLog(device, "Log_AdvancedChangeConfigSaved");
                }
                catch (OperationCanceledException)
                {
                    SetOperationCancellationLog(device, operation, "Log_AdvancedChangeConfigCanceled");
                }
                catch (Exception exception)
                {
                    _logger.LogError(exception, "Failed to configure advanced Change Device options for {Serial}.", device.Serial);
                    SetDeviceLog(device, "Log_AdvancedChangeConfigFailed");
                }
            }
        }

        [RelayCommand(CanExecute = nameof(CanExecuteSelectedDeviceAction), AllowConcurrentExecutions = true)]
        private async Task RandomChangeAndWipeDeviceAsync()
        {
            DeviceRowViewModel? selectedDevice = SelectedDevice;
            DeviceChangeOptions changeOptions = CreateCurrentChangeOptions();
            bool changeSimEnabled = IsChangeSimEnabled;
            RandomDeviceRequest randomRequest = CreateCurrentRandomDeviceRequest();
            IDeviceActionOperation? operation = await StartOnlineDeviceActionAsync(
                    selectedDevice,
                    DeviceActionKind.RandomChangeAndWipe)
                .ConfigureAwait(true);
            if (operation == null)
                return;

            using (operation)
            {
                DeviceRowViewModel device = selectedDevice!;
                CancellationToken cancellationToken = operation.CancellationToken;

                try
                {
                    bool confirmed = await _deviceActionConfirmationDialogService
                        .ConfirmChangeAndWipeAsync(
                            device.Name,
                            device.Serial,
                            changeOptions,
                            cancellationToken)
                        .ConfigureAwait(true);
                    if (!confirmed)
                    {
                        SetDialogDismissalLog(device, operation, "Log_ChangeDeviceCanceled");
                        return;
                    }

                    DeviceInfoApiDevice? profile;
                    try
                    {
                        SetDeviceLog(device, "Log_RandomDevice");
                        var randomResult = await _randomDeviceService
                            .CreateRandomProfileAsync(randomRequest, cancellationToken)
                            .ConfigureAwait(true);

                        if (randomResult.Status == RandomDeviceStatus.LoginRequired)
                        {
                            SetDeviceLog(device, "Log_RandomDeviceLoginRequired");
                            return;
                        }

                        if (randomResult.Status == RandomDeviceStatus.Failed || randomResult.Profile == null)
                        {
                            SetDeviceLog(device, "Log_RandomDeviceFailed");
                            return;
                        }

                        profile = randomResult.Profile.Clone();
                        ApplyRandomDeviceInfo(device.Serial, randomResult.Profile.Clone());
                    }
                    catch (OperationCanceledException)
                    {
                        SetOperationCancellationLog(device, operation, "Log_ChangeDeviceCanceled");
                        return;
                    }
                    catch (Exception exception)
                    {
                        _logger.LogError(exception, "Unexpected failure while randomizing device info.");
                        SetDeviceLog(device, "Log_RandomDeviceFailed");
                        return;
                    }
                    SetDeviceLog(device, "Log_ChangeDevice");

                    try
                    {
                        IProgress<DeviceChangeStage> progress = CreateDeviceChangeProgress(
                            device,
                            "Log_ChangeDevice",
                            "Log_ChangeDeviceSuccess");
                        await _deviceChangeService
                            .ChangeAsync(
                                device.Serial,
                                profile!,
                                changeSimEnabled,
                                changeOptions,
                                progress,
                                cancellationToken)
                            .ConfigureAwait(true);

                        SetDeviceLog(device, "Log_ChangeDeviceSuccess");
                    }
                    catch (OperationCanceledException)
                    {
                        SetOperationCancellationLog(device, operation, "Log_ChangeDeviceCanceled");
                    }
                    catch (Exception exception)
                    {
                        _logger.LogError(exception, "Failed to change device {Serial}.", device.Serial);
                        SetDeviceLog(device, "Log_ChangeDeviceFailed");
                    }
                }
                catch (OperationCanceledException)
                {
                    SetOperationCancellationLog(device, operation, "Log_ChangeDeviceCanceled");
                }
                catch (Exception exception)
                {
                    _logger.LogError(exception, "Failed to randomize and change device {Serial}.", device.Serial);
                    SetDeviceLog(device, "Log_ChangeDeviceFailed");
                }
            }
        }

        [RelayCommand(CanExecute = nameof(CanExecuteSelectedDeviceAction), AllowConcurrentExecutions = true)]
        private async Task RandomSimAsync()
        {
            DeviceRowViewModel? selectedDevice = SelectedDevice;
            CarrierCountryOption? country = CloneCountryOption(SelectedCountry);
            CarrierOption? carrier = CloneCarrierOption(SelectedCarrier);
            IDeviceActionOperation? operation = await StartOnlineDeviceActionAsync(
                    selectedDevice,
                    DeviceActionKind.RandomSim)
                .ConfigureAwait(true);
            if (operation == null)
                return;

            using (operation)
            {
                DeviceRowViewModel device = selectedDevice!;
                CancellationToken cancellationToken = operation.CancellationToken;

                try
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    SetDeviceLog(device, "Log_RandomSim");
                    SimProfile simProfile = _simProfileService.CreateRandomProfile(country, carrier);
                    ApplyRandomSimInfo(device.Serial, simProfile);
                    SetDeviceLog(device, "Log_RandomSimSuccess");
                }
                catch (OperationCanceledException)
                {
                    SetOperationCancellationLog(device, operation, "Log_RandomSimCanceled");
                }
                catch (Exception exception)
                {
                    _logger.LogError(exception, "Failed to generate random SIM information.");
                    SetDeviceLog(device, "Log_RandomSimFailed");
                }
            }
        }

        private async Task<DeviceRowViewModel?> GetDeviceAsync(
            DeviceRowViewModel? device,
            CancellationToken cancellationToken)
        {
            if (device == null)
            {
                await ShowToolbarLogAsync("Log_SelectDeviceFirst", cancellationToken).ConfigureAwait(true);
                return null;
            }

            SelectSingleDevice(device);
            return device;
        }

        private Task<DeviceRowViewModel?> GetSelectedOnlineDeviceAsync(CancellationToken cancellationToken)
        {
            return GetOnlineDeviceAsync(SelectedDevice, cancellationToken);
        }

        private async Task<DeviceRowViewModel?> GetOnlineDeviceAsync(
            DeviceRowViewModel? device,
            CancellationToken cancellationToken)
        {
            device = await GetDeviceAsync(device, cancellationToken).ConfigureAwait(true);
            if (device == null)
                return null;

            if (device.ConnectionStatus != AdbDeviceStatus.Online)
            {
                SetDeviceLog(device, "Log_DeviceMustBeOnline");
                return null;
            }

            bool isOnline;
            try
            {
                isOnline = await _deviceListService
                    .IsDeviceOnlineAsync(device.Serial, cancellationToken)
                    .ConfigureAwait(true);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                _logger.LogWarning(exception, "Live ADB preflight failed for device {Serial}.", device.Serial);
                isOnline = false;
            }

            if (isOnline)
            {
                return device;
            }

            SetDeviceLog(device, "Log_DeviceMustBeOnline");
            return null;
        }

        private DeviceInfoApiDevice? CreateRandomDeviceProfileSnapshot(DeviceRowViewModel? device)
        {
            if (device != null
                && _randomDeviceProfiles.TryGetValue(device.Serial, out DeviceInfoApiDevice? profile))
            {
                return profile.Clone();
            }

            return null;
        }

        [RelayCommand(CanExecute = nameof(CanExecuteSelectedDeviceAction), AllowConcurrentExecutions = true)]
        private async Task ChangeSimAsync()
        {
            DeviceRowViewModel? selectedDevice = SelectedDevice;
            SimProfile? sourceProfile = selectedDevice != null
                && _randomSimProfiles.TryGetValue(selectedDevice.Serial, out SimProfile? profile)
                ? profile
                : null;
            SimProfile? editedProfile = sourceProfile == null
                ? null
                : CreateEditedSimProfile(sourceProfile);
            if (selectedDevice != null && editedProfile == null)
            {
                SetDeviceLog(selectedDevice, "Log_RandomSimRequired");
                return;
            }

            IDeviceActionOperation? operation = await StartOnlineDeviceActionAsync(
                    selectedDevice,
                    DeviceActionKind.ChangeSim)
                .ConfigureAwait(true);
            if (operation == null)
                return;

            using (operation)
            {
                DeviceRowViewModel device = selectedDevice!;
                CancellationToken cancellationToken = operation.CancellationToken;

                try
                {
                    bool confirmed = await _deviceActionConfirmationDialogService
                        .ConfirmChangeSimAsync(device.Name, device.Serial, cancellationToken)
                        .ConfigureAwait(true);
                    if (!confirmed)
                    {
                        SetDialogDismissalLog(device, operation, "Log_ChangeSimCanceled");
                        return;
                    }

                    SetDeviceLog(device, "Log_ChangeSim");
                    await _deviceChangeService
                        .ChangeSimAsync(device.Serial, editedProfile!, cancellationToken)
                        .ConfigureAwait(true);
                    _randomSimProfiles[device.Serial] = editedProfile!;
                    SetDeviceLog(device, "Log_ChangeSimSuccess");
                }
                catch (OperationCanceledException)
                {
                    SetOperationCancellationLog(device, operation, "Log_ChangeSimCanceled");
                }
                catch (Exception exception)
                {
                    _logger.LogError(exception, "Failed to change SIM information on device {Serial}.", device.Serial);
                    SetDeviceLog(device, "Log_ChangeSimFailed");
                }
            }
        }

        [RelayCommand(CanExecute = nameof(CanExecuteSelectedDeviceAction), AllowConcurrentExecutions = true)]
        private async Task ChangeLocationAsync()
        {
            DeviceRowViewModel? selectedDevice = SelectedDevice;
            IDeviceActionOperation? operation = await StartOnlineDeviceActionAsync(
                    selectedDevice,
                    DeviceActionKind.ChangeLocation)
                .ConfigureAwait(true);
            if (operation == null)
                return;

            using (operation)
            {
                DeviceRowViewModel device = selectedDevice!;
                CancellationToken cancellationToken = operation.CancellationToken;

                SetDeviceLog(device, "Log_OpeningDialog");

                try
                {
                    var dialogResult = await _changeLocationDialogService
                        .ShowChangeLocationAsync(device.Serial, device.Name, cancellationToken)
                        .ConfigureAwait(true);

                    if (dialogResult == null)
                    {
                        SetDialogDismissalLog(device, operation, "Log_ChangeLocationCanceled");
                        return;
                    }

                    dialogResult = CloneLocationDialogResult(dialogResult);

                    SetDeviceLog(
                        device,
                        dialogResult.Mode == ChangeLocationMode.DeviceIp
                            ? "Log_ResolvingByIp"
                            : "Log_ApplyingLocation");
                    DeviceLocationResult locationResult = await _deviceLocationService
                        .ApplyAsync(device.Serial, dialogResult, cancellationToken)
                        .ConfigureAwait(true);

                    await SaveLocationConfigAsync(
                            device.Serial,
                            dialogResult.Mode,
                            locationResult.Latitude,
                            locationResult.Longitude,
                            locationResult.CountryCode,
                            locationResult.CityName,
                            cancellationToken)
                        .ConfigureAwait(true);

                    SetDeviceLog(device, "Log_ChangeLocationSuccess");
                }
                catch (OperationCanceledException)
                {
                    SetOperationCancellationLog(device, operation, "Log_ChangeLocationCanceled");
                }
                catch (Exception exception)
                {
                    _logger.LogError(exception, "Failed to change location for device {Serial}.", device.Serial);
                    SetDeviceLog(device, "Log_ChangeLocationFailed");
                }
            }
        }

        [RelayCommand(CanExecute = nameof(CanExecuteSelectedDeviceAction), AllowConcurrentExecutions = true)]
        private async Task ChangeTimezoneAsync()
        {
            DeviceRowViewModel? selectedDevice = SelectedDevice;
            IDeviceActionOperation? operation = await StartOnlineDeviceActionAsync(
                    selectedDevice,
                    DeviceActionKind.ChangeTimezone)
                .ConfigureAwait(true);
            if (operation == null)
                return;

            using (operation)
            {
                DeviceRowViewModel device = selectedDevice!;
                CancellationToken cancellationToken = operation.CancellationToken;

                SetDeviceLog(device, "Log_OpeningDialog");

                try
                {
                    var dialogResult = await _changeTimezoneDialogService
                        .ShowChangeTimezoneAsync(device.Serial, device.Name, cancellationToken)
                        .ConfigureAwait(true);

                    if (dialogResult == null)
                    {
                        SetDialogDismissalLog(device, operation, "Log_ChangeTimezoneCanceled");
                        return;
                    }

                    dialogResult = CloneTimezoneDialogResult(dialogResult);

                    SetDeviceLog(
                        device,
                        dialogResult.Mode == ChangeTimezoneMode.DeviceIp
                            ? "Log_ResolvingByIp"
                            : "Log_ApplyingTimezone");
                    string appliedTimezone = await _deviceTimezoneService
                        .ApplyAsync(device.Serial, dialogResult, cancellationToken)
                        .ConfigureAwait(true);

                    await SaveTimezoneConfigAsync(
                            device.Serial,
                            dialogResult.Mode,
                            appliedTimezone,
                            cancellationToken)
                        .ConfigureAwait(true);

                    SetDeviceLog(device, "Log_ChangeTimezoneSuccess");
                }
                catch (OperationCanceledException)
                {
                    SetOperationCancellationLog(device, operation, "Log_ChangeTimezoneCanceled");
                }
                catch (Exception exception)
                {
                    _logger.LogError(exception, "Failed to change timezone for device {Serial}.", device.Serial);
                    SetDeviceLog(device, "Log_ChangeTimezoneFailed");
                }
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
            return SelectedDevice != null
                && !IsDeviceBusy(SelectedDevice)
                && _randomDeviceProfiles.ContainsKey(SelectedDevice.Serial);
        }

        [RelayCommand(CanExecute = nameof(CanViewRandomDeviceInfo), AllowConcurrentExecutions = true)]
        private async Task ViewRandomDeviceInfoAsync()
        {
            CancellationToken cancellationToken = CancellationToken.None;
            DeviceRowViewModel? device = SelectedDevice;
            if (device == null
                || !_randomDeviceProfiles.TryGetValue(device.Serial, out DeviceInfoApiDevice? profile))
                return;

            DeviceInfoApiDevice profileSnapshot = profile.Clone();
            IDeviceActionOperation? operation = TryStartDeviceAction(
                device,
                DeviceActionKind.ViewRandomDeviceInfo,
                canCancel: false);
            if (operation == null)
                return;

            using (operation)
            {
                cancellationToken = operation.CancellationToken;
                try
                {
                    bool updated = await _randomDeviceInfoDialogService
                        .ShowRandomDeviceInfoAsync(profileSnapshot, cancellationToken)
                        .ConfigureAwait(true);
                    if (updated)
                        ApplyRandomDeviceInfo(device.Serial, profileSnapshot);
                }
                catch (OperationCanceledException)
                {
                    SetOperationCancellationLog(device, operation, "Log_ViewRandomDeviceInfoCanceled");
                }
            }
        }

        private Task SaveLocationConfigAsync(
            string serial,
            ChangeLocationMode mode,
            string latitude,
            string longitude,
            CancellationToken cancellationToken)
        {
            return SaveLocationConfigAsync(
                serial,
                mode,
                latitude,
                longitude,
                countryCode: string.Empty,
                cityName: string.Empty,
                cancellationToken);
        }

        private async Task SaveLocationConfigAsync(
            string serial,
            ChangeLocationMode mode,
            string latitude,
            string longitude,
            string countryCode,
            string cityName,
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
                        countryCode,
                        cityName,
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

        [RelayCommand(CanExecute = nameof(CanExecuteSelectedDeviceAction), AllowConcurrentExecutions = true)]
        private async Task UpdateIntegrityAsync()
        {
            DeviceRowViewModel? selectedDevice = SelectedDevice;
            StoredDeviceConfig? storedDeviceSnapshot = selectedDevice == null
                ? null
                : CreateStoredDeviceConfigSnapshot(selectedDevice.Serial);
            if (selectedDevice != null && storedDeviceSnapshot == null)
            {
                SetDeviceLog(selectedDevice, "Log_UpdateIntegrityFailed");
                return;
            }
            IDeviceActionOperation? operation = await StartOnlineDeviceActionAsync(
                    selectedDevice,
                    DeviceActionKind.UpdateIntegrity)
                .ConfigureAwait(true);
            if (operation == null)
                return;

            using (operation)
            {
                DeviceRowViewModel device = selectedDevice!;
                CancellationToken cancellationToken = operation.CancellationToken;

                SetDeviceLog(device, "Log_OpeningDialog");

                try
                {
                    var dialogResult = await _updateIntegrityDialogService
                        .ShowUpdateIntegrityAsync(
                            device.Serial,
                            device.Name,
                            storedDeviceSnapshot!,
                            (result, saveCancellationToken) => SaveUpdateIntegrityConfigAsync(device, result, saveCancellationToken),
                            cancellationToken)
                        .ConfigureAwait(true);

                    if (dialogResult == null)
                    {
                        SetDialogDismissalLog(device, operation, "Log_UpdateIntegrityCanceled");
                        return;
                    }

                    await SaveUpdateIntegrityConfigAsync(device, dialogResult, cancellationToken).ConfigureAwait(true);

                    SetDeviceLog(
                        device,
                        dialogResult.UpdateIntegrityEnabled
                            ? "Log_UpdatingIntegrity"
                            : "Log_UpdatingKeybox");
                    await _deviceIntegrityService
                        .ApplyAsync(device.Serial, dialogResult, cancellationToken)
                        .ConfigureAwait(true);

                    SetDeviceLog(device, "Log_UpdateIntegritySuccess");
                }
                catch (OperationCanceledException)
                {
                    SetOperationCancellationLog(device, operation, "Log_UpdateIntegrityCanceled");
                }
                catch (Exception exception)
                {
                    _logger.LogError(exception, "Failed to update integrity for device {Serial}.", device.Serial);
                    SetDeviceLog(device, "Log_UpdateIntegrityFailed");
                }
            }
        }

        [RelayCommand(CanExecute = nameof(CanExecuteSelectedDeviceAction), AllowConcurrentExecutions = true)]
        private async Task InstallApkAsync()
        {
            DeviceRowViewModel? selectedDevice = SelectedDevice;
            IDeviceActionOperation? operation = await StartOnlineDeviceActionAsync(
                    selectedDevice,
                    DeviceActionKind.InstallPackages)
                .ConfigureAwait(true);
            if (operation == null)
                return;

            using (operation)
            {
                DeviceRowViewModel device = selectedDevice!;
                CancellationToken cancellationToken = operation.CancellationToken;

                SetDeviceLog(device, "Log_InstallPackageOpening");

                try
                {
                    var dialogResult = await _installPackageDialogService
                        .ShowInstallPackageAsync(device.Serial, device.Name, cancellationToken)
                        .ConfigureAwait(true);

                    if (dialogResult == null)
                    {
                        SetDialogDismissalLog(device, operation, "Log_InstallPackageCanceled");
                        return;
                    }

                    var request = new InstallPackageRequest(
                        dialogResult.FilePaths.ToArray(),
                        new InstallPackageOptions(
                            dialogResult.Options.GrantPermissions,
                            dialogResult.Options.AllowDowngrade));

                    bool isOnline;
                    try
                    {
                        isOnline = await _deviceListService
                            .IsDeviceOnlineAsync(device.Serial, cancellationToken)
                            .ConfigureAwait(true);
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception exception)
                    {
                        _logger.LogWarning(
                            exception,
                            "Live ADB confirmation failed for device {Serial} after install dialog.",
                            device.Serial);
                        isOnline = false;
                    }

                    if (!isOnline)
                    {
                        SetDeviceLog(device, "Log_DeviceMustBeOnline");
                        return;
                    }

                    int successCount = 0;
                    int totalCount = request.FilePaths.Count;
                    InstallPackageResult? singlePackageResult = null;
                    foreach (string filePath in request.FilePaths)
                    {
                        SetDeviceLog(device, "Log_InstallPackageInstalling");

                        InstallPackageResult result;
                        try
                        {
                            result = await _packageInstallService
                                .InstallAsync(
                                    device.Serial,
                                    filePath,
                                    request.Options,
                                    cancellationToken)
                                .ConfigureAwait(true);
                        }
                        catch (OperationCanceledException)
                        {
                            throw;
                        }
                        catch (Exception exception)
                        {
                            _logger.LogError(
                                exception,
                                "Unexpected package installation failure for device {Serial}, file {FilePath}.",
                                device.Serial,
                                filePath);
                            result = new InstallPackageResult(
                                filePath,
                                false,
                                "Log_InstallPackageAdbFailure");
                        }

                        if (result.Success)
                            successCount++;
                        if (totalCount == 1)
                            singlePackageResult = result;
                    }

                    if (singlePackageResult is { } singleResult)
                    {
                        SetDeviceLog(
                            device,
                            singleResult.MessageResourceKey,
                            singleResult.MessageArguments.ToArray());
                    }
                    else
                    {
                        string summaryKey = successCount == totalCount
                            ? "Log_InstallPackageCompleteFormat"
                            : successCount > 0
                                ? "Log_InstallPackagePartialFormat"
                                : "Log_InstallPackageFailedFormat";
                        SetDeviceLog(device, summaryKey, successCount, totalCount);
                    }

                }
                catch (OperationCanceledException)
                {
                    SetOperationCancellationLog(device, operation, "Log_InstallPackageCanceled");
                }
                catch (Exception exception)
                {
                    _logger.LogError(exception, "Failed to install package for selected device.");
                    SetDeviceLog(device, "Log_InstallPackageAdbFailure");
                }
            }
        }

        [RelayCommand(CanExecute = nameof(CanExecuteSelectedDeviceAction), AllowConcurrentExecutions = true)]
        private async Task FakeProxyAsync()
        {
            DeviceRowViewModel? selectedDevice = SelectedDevice;
            IDeviceActionOperation? operation = await StartOnlineDeviceActionAsync(
                    selectedDevice,
                    DeviceActionKind.FakeProxy)
                .ConfigureAwait(true);
            if (operation == null)
                return;

            using (operation)
            {
                DeviceRowViewModel device = selectedDevice!;
                CancellationToken cancellationToken = operation.CancellationToken;

                SetDeviceLog(device, "Log_OpeningDialog");

                try
                {
                    var dialogResult = await _fakeProxyDialogService
                        .ShowFakeProxyDialogAsync(device.Serial, device.Name, cancellationToken)
                        .ConfigureAwait(true);

                    if (dialogResult == null)
                    {
                        SetDialogDismissalLog(device, operation, "Log_FakeProxyCanceled");
                        return;
                    }

                    SetDeviceLog(device, "Log_StartingProxy");

                    ProxyWorkflowResult workflowResult = await _proxyWorkflowService
                        .ApplyAsync(device.Serial, dialogResult, cancellationToken)
                        .ConfigureAwait(true);

                    if (workflowResult.LocationUpdateFailed)
                    {
                        SetDeviceLog(device, "Log_ProxyLocationByIpFailed");
                    }

                    if (workflowResult.TimezoneUpdateFailed)
                    {
                        SetDeviceLog(device, "Log_ProxyTimezoneByIpFailed");
                    }

                    bool postProxyUpdatesSucceeded =
                        !workflowResult.LocationUpdateFailed && !workflowResult.TimezoneUpdateFailed;

                    SetDeviceLog(
                        device,
                        postProxyUpdatesSucceeded
                            ? "Log_FakeProxySuccess"
                            : "Log_FakeProxyPartialSuccess");
                }
                catch (OperationCanceledException)
                {
                    SetOperationCancellationLog(device, operation, "Log_FakeProxyCanceled");
                }
                catch (Exception exception)
                {
                    _logger.LogError(exception, "Failed to apply fake proxy for device {Serial}.", device.Serial);
                    SetDeviceLog(device, "Log_FakeProxyFailed");
                }
            }
        }

        [RelayCommand(CanExecute = nameof(CanExecuteSelectedDeviceAction), AllowConcurrentExecutions = true)]
        private async Task StopFakeProxyAsync()
        {
            DeviceRowViewModel? selectedDevice = SelectedDevice;
            IDeviceActionOperation? operation = await StartOnlineDeviceActionAsync(
                    selectedDevice,
                    DeviceActionKind.StopFakeProxy)
                .ConfigureAwait(true);
            if (operation == null)
                return;

            using (operation)
            {
                DeviceRowViewModel device = selectedDevice!;
                CancellationToken cancellationToken = operation.CancellationToken;

                SetDeviceLog(device, "Log_StoppingProxy");

                try
                {
                    await _adbProxyService
                        .StopProxyAsync(device.Serial, cancellationToken)
                        .ConfigureAwait(true);

                    SetDeviceLog(device, "Log_StopFakeProxySuccess");
                }
                catch (OperationCanceledException)
                {
                    SetOperationCancellationLog(device, operation, "Log_StopFakeProxyCanceled");
                }
                catch (Exception exception)
                {
                    _logger.LogError(exception, "Failed to stop fake proxy for device {Serial}.", device.Serial);
                    SetDeviceLog(device, "Log_StopFakeProxyFailed");
                }
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
        private async Task SaveSingleDeviceColumnRatiosAsync(
            IReadOnlyDictionary<string, double>? ratios,
            CancellationToken cancellationToken)
        {
            if (ratios == null || ratios.Count == 0)
                return;

            DeviceTableColumnRatioHelper.Replace(_settings.DeviceTableColumnRatios, ratios);

            OnPropertyChanged(nameof(DeviceTableColumnRatios));
            await SaveAppSettingsAsync(cancellationToken).ConfigureAwait(false);
        }

        private async Task SaveAppSettingsAsync(CancellationToken cancellationToken)
        {
            try
            {
                await _settingsService.SaveAsync(_settings, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to save Single Device table layout.");
            }
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
                        _localizationService.GetString("ChangeSingleDevice_NewDeviceCount"),
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
                        _localizationService.GetString("ChangeSingleDevice_NewDeviceCount"),
                        _localizationService.GetString("ChangeSingleDevice_NotAvailable"));
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

            var wasRefreshingRows = _isRefreshingRows;
            _isRefreshingRows = true;
            try
            {
                foreach (var device in _allDeviceRows)
                {
                    device.ConnectionStatus = connectedBySerial.TryGetValue(device.Serial, out var connectedDevice)
                        ? connectedDevice.Status
                        : AdbDeviceStatus.Offline;
                    device.Status = GetConnectionStatusText(device.ConnectionStatus);
                }
            }
            finally
            {
                _isRefreshingRows = wasRefreshingRows;
            }

            ApplyDeviceFilter();
            NotifyDeviceInteractionChanged();
        }

        private void RefreshDeviceRows(IReadOnlyList<StoredDeviceConfig> storedDevices, IReadOnlyList<AdbDevice> connectedDevices)
        {
            _isRefreshingRows = true;
            var targetSerial = SelectedDevice?.Serial ?? _settings.SelectedSingleDeviceSerial;

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
            var targetSerial = SelectedDevice?.Serial ?? _settings.SelectedSingleDeviceSerial;

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
            string? selectedSerial = _deviceListService.FindSelectionSerial(
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
            var matchesFilter = SelectedDeviceFilter switch
            {
                "Online" => device.ConnectionStatus == AdbDeviceStatus.Online,
                "Offline" => device.ConnectionStatus != AdbDeviceStatus.Online,
                "Active" => string.Equals(device.Active, "Active", StringComparison.OrdinalIgnoreCase),
                "Inactive" => string.Equals(device.Active, "Inactive", StringComparison.OrdinalIgnoreCase),
                _ => true
            };
            if (!matchesFilter)
                return false;

            var search = DeviceSearchText.Trim();
            return search.Length == 0
                || device.Serial.Contains(search, StringComparison.OrdinalIgnoreCase)
                || device.Name.Contains(search, StringComparison.OrdinalIgnoreCase)
                || device.Type.Contains(search, StringComparison.OrdinalIgnoreCase);
        }

        private void ReapplySearchIfActive()
        {
            if (!string.IsNullOrWhiteSpace(DeviceSearchText))
                ApplyDeviceFilter();
        }

        private void OnDeviceRowPropertyChanged(object? sender, PropertyChangedEventArgs args)
        {
            if (_isRefreshingRows || sender is not DeviceRowViewModel deviceRow)
                return;

            if (args.PropertyName == nameof(DeviceRowViewModel.IsSelected))
            {
                if (!_isSynchronizingSelection)
                {
                    if (deviceRow.IsSelected)
                        SelectSingleDevice(deviceRow);
                    else if (ReferenceEquals(_selectedDevice, deviceRow))
                        SelectSingleDevice(null);
                }

                return;
            }

            if (args.PropertyName == nameof(DeviceRowViewModel.Name))
            {
                QueueDeviceRowEdit(deviceRow);
                ReapplySearchIfActive();
                return;
            }

            if (args.PropertyName == nameof(DeviceRowViewModel.Type))
            {
                CancelPendingDeviceEdit(deviceRow.Serial);
                TrackSilentSave(
                    SaveDeviceRowEditAsync(CreateDeviceRowEditSnapshot(deviceRow), GetActiveToken()),
                    "Failed to save device row edit.");
                ReapplySearchIfActive();
                return;
            }

            if (args.PropertyName == nameof(DeviceRowViewModel.ConnectionStatus))
            {
                ApplyDeviceFilter();
                NotifyDeviceInteractionChanged();
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
                foreach (DeviceRowViewModel device in _allDeviceRows)
                    device.IsSelected = ReferenceEquals(device, selectedDevice);

                SetProperty(ref _selectedDevice, selectedDevice, nameof(SelectedDevice));
            }
            finally
            {
                _isSynchronizingSelection = false;
            }

            if (serialChanged)
            {
                _settings.SelectedSingleDeviceSerial = selectedSerial;
                TrackSilentSave(SaveSettingsAsync(GetActiveToken()), "Failed to save selected device setting.");
            }

            if (referenceChanged)
            {
                ApplyStoredDeviceConfig(selectedDevice);
                ApplySelectedDeviceInfo(selectedDevice);
            }

            NotifyDeviceInteractionChanged();
        }

        private void ApplySelectedDeviceInfo(DeviceRowViewModel? selectedDevice)
        {
            if (selectedDevice != null
                && _randomDeviceProfiles.TryGetValue(selectedDevice.Serial, out DeviceInfoApiDevice? profile))
            {
                DisplayDeviceInfo(profile);
            }
            else
            {
                DisplayDeviceInfo(new DeviceInfoApiDevice());
            }

            ViewRandomDeviceInfoCommand.NotifyCanExecuteChanged();
        }

        private void QueueDeviceRowEdit(DeviceRowViewModel deviceRow)
        {
            var cancellation = new CancellationTokenSource();
            var pendingEdit = new PendingDeviceEdit(
                CreateDeviceRowEditSnapshot(deviceRow),
                cancellation);

            lock (_pendingDeviceEditsLock)
            {
                if (_pendingDeviceEdits.Remove(deviceRow.Serial, out var previousEdit))
                    previousEdit.Cancellation.Cancel();

                _pendingDeviceEdits[deviceRow.Serial] = pendingEdit;
                pendingEdit.PersistenceTask = PersistDeviceRowEditAfterDelayAsync(pendingEdit);
            }
        }

        private DeviceRowEditSnapshot CreateDeviceRowEditSnapshot(DeviceRowViewModel deviceRow)
        {
            bool includeSelectedCarrierConfig = ReferenceEquals(deviceRow, SelectedDevice);
            return new DeviceRowEditSnapshot(
                deviceRow.Serial,
                deviceRow.Name,
                deviceRow.Type,
                includeSelectedCarrierConfig,
                includeSelectedCarrierConfig ? CloneCountryOption(SelectedCountry) : null,
                includeSelectedCarrierConfig ? CloneCarrierOption(SelectedCarrier) : null);
        }

        private async Task PersistDeviceRowEditAfterDelayAsync(PendingDeviceEdit pendingEdit)
        {
            try
            {
                await Task.Delay(DeviceNameSaveDebounceMilliseconds, pendingEdit.Cancellation.Token).ConfigureAwait(false);
                await SaveDeviceRowEditAsync(pendingEdit.Snapshot, pendingEdit.Cancellation.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (pendingEdit.Cancellation.IsCancellationRequested)
            {
            }
            finally
            {
                lock (_pendingDeviceEditsLock)
                {
                    if (_pendingDeviceEdits.TryGetValue(pendingEdit.Snapshot.Serial, out var currentEdit)
                        && ReferenceEquals(currentEdit, pendingEdit))
                    {
                        _pendingDeviceEdits.Remove(pendingEdit.Snapshot.Serial);
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
                await SaveDeviceRowEditAsync(pendingEdit.Snapshot, CancellationToken.None).ConfigureAwait(false);
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

                SelectedBrand = FindOption(Brands, storedDevice?.Brand) ?? "Random";
                UpdateAndroidVersionOptions(SelectedBrand, storedDevice?.AndroidVersion);
                IsChangeSimEnabled = storedDevice?.ChangeSimEnabled ?? true;
                _useIntegritySecurityPatch = storedDevice?.UseIntegritySecurityPatch ?? true;
                _deviceChangeOptions = DeviceChangeOptionsHelper.CreateNormalizedCopy(
                    storedDevice?.ChangeOptions ?? new DeviceChangeOptions());
                UseDefaultChangeMode = _deviceChangeOptions.UseDefaultMode;
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
            AndroidVersions.Clear();
            foreach (string version in DeviceProfileOptionsHelper.GetAndroidVersions(brand))
                AndroidVersions.Add(version);

            SelectedAndroidVersion = FindOption(AndroidVersions, preferredVersion)
                ?? "Random";
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
                UseIntegritySecurityPatch = _useIntegritySecurityPatch,
                CountryIso = SelectedCountry?.CountryIso ?? string.Empty,
                CountryName = SelectedCountry?.CountryName ?? string.Empty,
                Carrier = SelectedCarrier?.CarrierName ?? string.Empty,
                CarrierMcc = SelectedCarrier?.Mcc ?? string.Empty,
                CarrierMnc = SelectedCarrier?.Mnc ?? string.Empty,
                ChangeOptions = DeviceChangeOptionsHelper.CreateNormalizedCopy(
                    _deviceChangeOptions,
                    UseDefaultChangeMode)
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
            await SaveDeviceProfileAsync(pendingEdit.Serial, pendingEdit.Profile, cancellationToken)
                .ConfigureAwait(false);
        }

        private async Task SaveDeviceProfileAsync(
            string serial,
            DeviceProfileConfig profile,
            CancellationToken cancellationToken)
        {
            try
            {
                await _deviceRefreshLock.WaitAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    await _deviceConfigService
                        .SaveDeviceProfileAsync(
                            _storedDevices,
                            serial,
                            profile,
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

        private async Task SaveDeviceRowEditAsync(
            DeviceRowEditSnapshot snapshot,
            CancellationToken cancellationToken)
        {
            await SaveDeviceConfigAsync(snapshot, cancellationToken).ConfigureAwait(false);
        }

        private async Task SaveDeviceConfigAsync(
            DeviceRowEditSnapshot snapshot,
            CancellationToken cancellationToken)
        {
            try
            {
                await _deviceRefreshLock.WaitAsync(cancellationToken).ConfigureAwait(false);

                try
                {
                    await _deviceConfigService
                        .SaveDeviceRowAsync(
                            _storedDevices,
                            snapshot.Serial,
                            snapshot.Name,
                            snapshot.Type,
                            snapshot.Country,
                            snapshot.Carrier,
                            snapshot.IncludeSelectedCarrierConfig,
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
            DeviceRowViewModel deviceRow = DeviceRowFactory.CreateDeviceRow(
                index,
                storedDevice,
                connectedDevice,
                GetConnectionStatusText(connectionStatus),
                GetLogText("Log_Ready"));
            deviceRow.IsActionBusy = _deviceActionCoordinatorService.IsBusy(deviceRow.Serial);
            if (_deviceProcessStateService.Get(deviceRow.Serial) is { } process)
                deviceRow.RestoreProcess(process.Message, process.State);
            return deviceRow;
        }

        private string GetConnectionStatusText(AdbDeviceStatus status)
        {
            string resourceKey = status switch
            {
                AdbDeviceStatus.Online => "ChangeSingleDevice_StatusOnline",
                AdbDeviceStatus.Unauthorized => "ChangeSingleDevice_StatusUnauthorized",
                _ => "ChangeSingleDevice_StatusOffline"
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

        private void ApplyRandomDeviceInfo(string serial, DeviceInfoApiDevice randomDevice)
        {
            _randomDeviceProfiles[serial] = randomDevice;
            SimProfile? simProfile = CreateSimProfile(randomDevice);
            if (simProfile == null)
                _randomSimProfiles.Remove(serial);
            else
                _randomSimProfiles[serial] = simProfile;

            ViewRandomDeviceInfoCommand.NotifyCanExecuteChanged();
            if (SelectedDevice == null || !SerialEquals(SelectedDevice.Serial, serial))
                return;

            DisplayDeviceInfo(randomDevice);
        }

        private void DisplayDeviceInfo(DeviceInfoApiDevice randomDevice)
        {
            SynchronizeDeviceInfo(() =>
            {
                DeviceInfo.Name = GetFirstValue(randomDevice.Name, randomDevice.Board, randomDevice.Code);
                DeviceInfo.Hardware = randomDevice.Hardware ?? string.Empty;
                DeviceInfo.Fingerprint = randomDevice.Fingerprint ?? string.Empty;
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
            });
        }

        private void ApplyRandomSimInfo(string serial, SimProfile simProfile)
        {
            _randomSimProfiles[serial] = simProfile;
            if (_randomDeviceProfiles.TryGetValue(serial, out DeviceInfoApiDevice? randomDevice))
            {
                randomDevice.Iccid = simProfile.Iccid;
                randomDevice.Imsi = simProfile.Imsi;
                randomDevice.SimPhoneNumber = simProfile.PhoneNumber;
                randomDevice.SimOperatorNumeric = simProfile.OperatorNumeric;
                randomDevice.SimOperatorCountry = simProfile.OperatorCountry;
                randomDevice.SimOperatorName = simProfile.OperatorName;
            }

            if (SelectedDevice == null || !SerialEquals(SelectedDevice.Serial, serial))
                return;

            SynchronizeDeviceInfo(() =>
            {
                DeviceInfo.Iccid = simProfile.Iccid;
                DeviceInfo.Imsi = simProfile.Imsi;
                DeviceInfo.Operator = string.IsNullOrWhiteSpace(simProfile.OperatorName)
                    ? simProfile.OperatorNumeric
                    : simProfile.OperatorName;
                DeviceInfo.PhoneNumber = simProfile.PhoneNumber;
            });
        }

        private void SynchronizeDeviceInfo(Action update)
        {
            _isSynchronizingDeviceInfo = true;
            try
            {
                update();
            }
            finally
            {
                _isSynchronizingDeviceInfo = false;
            }
        }

        private void OnDeviceInfoPropertyChanged(object? _, PropertyChangedEventArgs __)
        {
            DeviceRowViewModel? selectedDevice = SelectedDevice;
            if (_isSynchronizingDeviceInfo
                || selectedDevice == null
                || !_randomDeviceProfiles.TryGetValue(selectedDevice.Serial, out DeviceInfoApiDevice? profile))
                return;

            CopyFormValuesToProfile(profile);
        }

        private SimProfile CreateEditedSimProfile(SimProfile profile)
        {
            return new SimProfile
            {
                Iccid = DeviceInfo.Iccid.Trim(),
                Imsi = DeviceInfo.Imsi.Trim(),
                PhoneNumber = DeviceInfo.PhoneNumber.Trim(),
                OperatorName = DeviceInfo.Operator.Trim(),
                OperatorCountry = profile.OperatorCountry,
                OperatorNumeric = profile.OperatorNumeric
            };
        }

        private static SimProfile? CreateSimProfile(DeviceInfoApiDevice profile)
        {
            if (string.IsNullOrWhiteSpace(profile.Iccid)
                || string.IsNullOrWhiteSpace(profile.Imsi)
                || string.IsNullOrWhiteSpace(profile.SimOperatorCountry)
                || string.IsNullOrWhiteSpace(profile.SimOperatorNumeric))
            {
                return null;
            }

            return new SimProfile
            {
                Iccid = profile.Iccid,
                Imsi = profile.Imsi,
                PhoneNumber = profile.SimPhoneNumber,
                OperatorName = profile.SimOperatorName,
                OperatorCountry = profile.SimOperatorCountry,
                OperatorNumeric = profile.SimOperatorNumeric
            };
        }

        private static string GetAndroidVersionDisplay(string? release, string? sdk)
        {
            if (!string.IsNullOrWhiteSpace(release))
                return release.StartsWith("Android ", StringComparison.OrdinalIgnoreCase)
                    ? release
                    : string.Concat("Android ", release.Trim());

            return sdk?.Trim() switch
            {
                "33" => "Android 13",
                "34" => "Android 14",
                "35" => "Android 15",
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
                Hardware = string.Empty,
                Fingerprint = string.Empty,
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

        private void CopyFormValuesToProfile(DeviceInfoApiDevice profile)
        {
            profile.Name = DeviceInfo.Name.Trim();
            profile.Hardware = DeviceInfo.Hardware.Trim();
            profile.Fingerprint = DeviceInfo.Fingerprint.Trim();
            profile.Model = DeviceInfo.Model.Trim();
            profile.Brand = DeviceInfo.Brand.Trim();
            profile.Release = DeviceInfo.AndroidVersion
                .Replace("Android ", string.Empty, StringComparison.OrdinalIgnoreCase)
                .Trim();
            profile.Serial = DeviceInfo.Serial.Trim();
            profile.Imei = DeviceInfo.Imei.Trim();
            profile.Iccid = DeviceInfo.Iccid.Trim();
            profile.Imsi = DeviceInfo.Imsi.Trim();
            profile.SimOperatorName = DeviceInfo.Operator.Trim();
            profile.SimPhoneNumber = DeviceInfo.PhoneNumber.Trim();
            profile.WifiMacAddress = DeviceInfo.Mac.Trim();
        }

        private RandomDeviceRequest CreateCurrentRandomDeviceRequest()
        {
            return new RandomDeviceRequest
            {
                SelectedBrand = SelectedBrand,
                SelectedAndroidVersion = SelectedAndroidVersion,
                UseIntegritySecurityPatch = UseDefaultChangeMode || _useIntegritySecurityPatch,
                Country = CloneCountryOption(SelectedCountry),
                Carrier = CloneCarrierOption(SelectedCarrier)
            };
        }

        private static CarrierCountryOption? CloneCountryOption(CarrierCountryOption? country)
        {
            return country == null
                ? null
                : new CarrierCountryOption(country.CountryIso, country.CountryCode, country.CountryName);
        }

        private static CarrierOption? CloneCarrierOption(CarrierOption? carrier)
        {
            return carrier == null
                ? null
                : new CarrierOption(carrier.CarrierName, carrier.Mcc, carrier.Mnc);
        }

        private static ChangeLocationDialogResult CloneLocationDialogResult(
            ChangeLocationDialogResult result)
        {
            LocationOption? location = result.SelectedLocation;
            LocationOption? locationCopy = location == null
                ? null
                : new LocationOption(
                    location.CountryCode,
                    location.CountryName,
                    location.CityName,
                    location.Timezone,
                    location.GmtOffset,
                    location.Latitude,
                    location.Longitude);
            return new ChangeLocationDialogResult(
                result.Mode,
                result.Latitude,
                result.Longitude,
                locationCopy);
        }

        private static ChangeTimezoneDialogResult CloneTimezoneDialogResult(
            ChangeTimezoneDialogResult result)
        {
            return new ChangeTimezoneDialogResult(result.Mode, result.Timezone);
        }

        private static StoredDeviceConfig CloneStoredDeviceConfig(StoredDeviceConfig source)
        {
            return new StoredDeviceConfig
            {
                Serial = source.Serial,
                Name = source.Name,
                Type = source.Type,
                CountryIso = source.CountryIso,
                CountryName = source.CountryName,
                Carrier = source.Carrier,
                CarrierMcc = source.CarrierMcc,
                CarrierMnc = source.CarrierMnc,
                Brand = source.Brand,
                AndroidVersion = source.AndroidVersion,
                ChangeSimEnabled = source.ChangeSimEnabled,
                UseIntegritySecurityPatch = source.UseIntegritySecurityPatch,
                ChangeOptions = DeviceChangeOptionsHelper.CreateNormalizedCopy(source.ChangeOptions),
                UpdateIntegrityFromServer = source.UpdateIntegrityFromServer,
                UpdateIntegrityFile = source.UpdateIntegrityFile,
                UpdateKeyboxFile = source.UpdateKeyboxFile,
                UpdateIntegrityEnabled = source.UpdateIntegrityEnabled,
                UpdateKeyboxEnabled = source.UpdateKeyboxEnabled,
                LocationMode = source.LocationMode,
                LocationLatitude = source.LocationLatitude,
                LocationLongitude = source.LocationLongitude,
                LocationCountryCode = source.LocationCountryCode,
                LocationCityName = source.LocationCityName,
                TimezoneMode = source.TimezoneMode,
                Timezone = source.Timezone,
                ProxyFullString = source.ProxyFullString,
                ProxyType = source.ProxyType,
                ProxyChangeLocationByIp = source.ProxyChangeLocationByIp,
                ProxyChangeTimezoneByIp = source.ProxyChangeTimezoneByIp
            };
        }

        private StoredDeviceConfig? CreateStoredDeviceConfigSnapshot(string serial)
        {
            StoredDeviceConfig? storedDevice = _storedDevices
                .FirstOrDefault(device => SerialEquals(device.Serial, serial));
            return storedDevice == null
                ? null
                : CloneStoredDeviceConfig(storedDevice);
        }

        private DeviceChangeOptions CreateCurrentChangeOptions()
        {
            return DeviceChangeOptionsHelper.CreateNormalizedCopy(
                _deviceChangeOptions,
                UseDefaultChangeMode);
        }

        private IProgress<DeviceChangeStage> CreateDeviceChangeProgress(
            DeviceRowViewModel device,
            string actionLogKey,
            string completedLogKey)
        {
            return new Progress<DeviceChangeStage>(stage =>
                SetDeviceLog(device, GetDeviceChangeLogKey(stage, actionLogKey, completedLogKey)));
        }

        private static string GetDeviceChangeLogKey(
            DeviceChangeStage stage,
            string actionLogKey,
            string completedLogKey)
        {
            return stage switch
            {
            DeviceChangeStage.Preparing => "Log_ChangeDevicePreparing",
            DeviceChangeStage.ApplyingProfile => "Log_ChangeDeviceApplyingProfile",
            DeviceChangeStage.ClearingData => "Log_ChangeDeviceClearingData",
            DeviceChangeStage.Rebooting => "Log_ChangeDeviceRebooting",
                DeviceChangeStage.WaitingForDevice => "Log_WaitingForDevice",
            DeviceChangeStage.Verifying => "Log_ChangeDeviceVerifying",
                DeviceChangeStage.Completed => completedLogKey,
                _ => actionLogKey
            };
        }

        private sealed record DeviceRowEditSnapshot(
            string Serial,
            string Name,
            string Type,
            bool IncludeSelectedCarrierConfig,
            CarrierCountryOption? Country,
            CarrierOption? Carrier);

        private sealed class PendingDeviceEdit
        {
            public PendingDeviceEdit(DeviceRowEditSnapshot snapshot, CancellationTokenSource cancellation)
            {
                Snapshot = snapshot;
                Cancellation = cancellation;
            }

            public DeviceRowEditSnapshot Snapshot { get; }
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
