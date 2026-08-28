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
        private readonly IDeviceActionEligibilityService _deviceActionEligibilityService;
        private readonly IDeviceActionFeedbackService _deviceActionFeedbackService;
        private readonly IClipboardService _clipboardService;
        private readonly IViewDeviceWindowService _viewDeviceWindowService;
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
        private readonly ObservableCollection<DeviceRowViewModel> _selectedDevices = [];
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
            ILogger<ChangeSingleDeviceViewModel> logger,
            IDeviceActionEligibilityService deviceActionEligibilityService,
            IDeviceActionFeedbackService deviceActionFeedbackService,
            IClipboardService clipboardService,
            IViewDeviceWindowService viewDeviceWindowService)
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
            _deviceActionEligibilityService = deviceActionEligibilityService;
            _deviceActionFeedbackService = deviceActionFeedbackService;
            _clipboardService = clipboardService;
            _viewDeviceWindowService = viewDeviceWindowService;
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
            SelectedDevices = new ReadOnlyObservableCollection<DeviceRowViewModel>(_selectedDevices);
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
        public ReadOnlyObservableCollection<DeviceRowViewModel> SelectedDevices { get; }
        public DeviceInfoFormViewModel DeviceInfo { get; }
        public IReadOnlyList<string> Brands { get; }
        public ObservableCollection<string> AndroidVersions { get; }
        public ObservableCollection<CarrierCountryOption> Countries { get; }
        public ObservableCollection<CarrierOption> Carriers { get; }
        public IReadOnlyList<string> TypeOptions { get; }
        public IReadOnlyDictionary<string, double> DeviceTableColumnRatios =>
            _settings.DeviceTableColumnRatios;
        public bool CanEditSelectedDeviceConfiguration =>
            SelectedDevice != null && !IsDeviceBusy(SelectedDevice);

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
            _deviceActionFeedbackService.SetProcess(device.Serial, resourceKey, formatArguments);
        }

        private void SetContextDeviceLog(
            DeviceRowViewModel device,
            string resourceKey,
            params object[] formatArguments)
        {
            _deviceActionFeedbackService.SetNonOwningProcess(
                device.Serial,
                resourceKey,
                formatArguments);
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
            return SelectedDevices.Count == 1;
        }

        private DeviceRowViewModel? GetSingleSelectedDeviceSnapshot()
        {
            return SelectedDevices.Count == 1 ? SelectedDevices[0] : null;
        }

        private async Task<bool> IsOperationTargetOnlineAsync(
            DeviceRowViewModel device,
            CancellationToken cancellationToken)
        {
            DeviceActionEligibilityFailure failure = await _deviceActionEligibilityService
                .CheckAsync(
                    device.Serial,
                    DeviceActionRequirement.Online,
                    cancellationToken)
                .ConfigureAwait(true);
            ApplyLiveConnectionState(device, failure);
            if (failure == DeviceActionEligibilityFailure.None)
                return true;

            _deviceActionFeedbackService.ReportEligibilityFailure(device.Serial, failure);
            return false;
        }

        private bool IsDeviceBusy(DeviceRowViewModel device)
        {
            return _deviceActionCoordinatorService.IsBusy(device.Serial);
        }

        private async Task<bool> CheckInitialOnlineIdleEligibilityAsync(DeviceRowViewModel? device)
        {
            if (device == null)
            {
                await ShowToolbarLogAsync("Log_SelectDeviceFirst", CancellationToken.None)
                    .ConfigureAwait(true);
                return false;
            }

            if (SelectedDevice == null || !SerialEquals(SelectedDevice.Serial, device.Serial))
                return false;

            DeviceActionEligibilityFailure eligibility = await _deviceActionEligibilityService
                .CheckAsync(
                    device.Serial,
                    DeviceActionRequirement.Online | DeviceActionRequirement.Idle,
                    _actionLifetimeCancellation.Token)
                .ConfigureAwait(true);
            ApplyLiveConnectionState(device, eligibility);
            if (eligibility == DeviceActionEligibilityFailure.None)
                return true;

            _deviceActionFeedbackService.ReportEligibilityFailure(device.Serial, eligibility);
            return false;
        }

        private IDeviceActionOperation? TryStartEligibleDeviceAction(
            DeviceRowViewModel device,
            DeviceActionKind kind)
        {
            // The initial preflight intentionally happens before loading the
            // mutable form snapshot. Do not reserve the prior row if the user
            // selected another device while that asynchronous load was running.
            if (SelectedDevice == null || !SerialEquals(SelectedDevice.Serial, device.Serial))
                return null;

            IDeviceActionOperation? operation = TryStartDeviceAction(device, kind);
            if (operation == null)
                ShowBusyActionLog(device);

            return operation;
        }

        private async Task<bool> RefreshSingleActionConfigurationSnapshotAsync(string serial)
        {
            if (SelectedDevice == null || !SerialEquals(SelectedDevice.Serial, serial))
                return false;

            DeviceProfileConfig profileSnapshot = CreateDeviceProfileConfig();
            try
            {
                await FlushPendingDeviceEditsAsync().ConfigureAwait(false);
                await FlushPendingDeviceProfileAsync().ConfigureAwait(false);

                if (SelectedDevice == null || !SerialEquals(SelectedDevice.Serial, serial))
                    return false;

                bool profileSaved = await SaveDeviceProfileAsync(
                        serial,
                        profileSnapshot,
                        CancellationToken.None)
                    .ConfigureAwait(false);
                if (!profileSaved)
                {
                    _deviceActionFeedbackService.SetNonOwningProcess(
                        serial,
                        "Log_ActionConfigurationSaveFailed");
                    return false;
                }
            }
            catch (Exception exception)
            {
                _logger.LogWarning(
                    exception,
                    "Failed to refresh the action configuration snapshot for device {Serial}.",
                    serial);
                _deviceActionFeedbackService.SetNonOwningProcess(
                    serial,
                    "Log_ActionConfigurationSaveFailed");
                return false;
            }

            return SelectedDevice != null && SerialEquals(SelectedDevice.Serial, serial);
        }

        private void ShowBusyActionLog(DeviceRowViewModel device)
        {
            _deviceActionFeedbackService.ReportEligibilityFailure(
                device.Serial,
                DeviceActionEligibilityFailure.Busy);
        }

        private Task SetOperationCancellationLogAsync(
            DeviceRowViewModel device,
            IDeviceActionOperation operation,
            bool requiresOnline)
        {
            return _deviceActionFeedbackService.ReportOperationCanceledAsync(
                device.Serial,
                operation.CancellationReason,
                requiresOnline,
                CancellationToken.None);
        }

        private async Task SetDialogDismissalLogAsync(
            DeviceRowViewModel device,
            IDeviceActionOperation operation)
        {
            if (operation.CancellationReason == DeviceActionCancellationReason.None)
            {
                _deviceActionFeedbackService.ReportDialogDismissed(device.Serial);
                return;
            }

            await SetOperationCancellationLogAsync(device, operation, requiresOnline: true);
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
                _actionLifetimeCancellation.Token,
                source: DeviceActionSource.SingleDevice);
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
                bool isBusy = _deviceActionCoordinatorService.IsBusy(snapshot.Serial);
                foreach (DeviceRowViewModel device in _allDeviceRows.Where(device => SerialEquals(device.Serial, snapshot.Serial)))
                {
                    device.RestoreAction(
                        isBusy
                            ? _deviceActionCoordinatorService.GetOperation(snapshot.Serial)
                            : null);
                }

                if (!isBusy
                    && _deviceProcessStateService.Get(snapshot.Serial) is
                    {
                        State: DeviceProcessState.InProgress
                    })
                {
                    _deviceProcessStateService.SetProcess(
                        snapshot.Serial,
                        GetLogText("Log_Ready"),
                        "Log_Ready");
                }

                bool selectedDeviceChangedBusy = SelectedDevice != null
                    && SerialEquals(SelectedDevice.Serial, snapshot.Serial);
                if (selectedDeviceChangedBusy)
                    NotifyActionPresentationChanged();
                else
                    StopDeviceActionCommand.NotifyCanExecuteChanged();
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

        private void NotifySelectionChanged()
        {
            NotifySelectedDeviceActionsCanExecuteChanged();
            NotifyActionPresentationChanged();
        }

        private void NotifySelectedDeviceActionsCanExecuteChanged()
        {
            RandomizeSingleDeviceInfoCommand.NotifyCanExecuteChanged();
            ChangeAndWipeSingleDeviceCommand.NotifyCanExecuteChanged();
            ChangeSingleDeviceWithoutWipeCommand.NotifyCanExecuteChanged();
            WipeSingleDeviceWithoutChangeCommand.NotifyCanExecuteChanged();
            OpenSingleDeviceAdvancedConfigCommand.NotifyCanExecuteChanged();
            RandomizeChangeAndWipeSingleDeviceCommand.NotifyCanExecuteChanged();
            RandomizeSingleDeviceSimInfoCommand.NotifyCanExecuteChanged();
            ChangeSingleDeviceSimInfoCommand.NotifyCanExecuteChanged();
            ChangeSingleDeviceLocationCommand.NotifyCanExecuteChanged();
            ChangeSingleDeviceTimezoneCommand.NotifyCanExecuteChanged();
            UpdateSingleDeviceIntegrityCommand.NotifyCanExecuteChanged();
            InstallPackagesOnSingleDeviceCommand.NotifyCanExecuteChanged();
            StartSingleDeviceFakeProxyCommand.NotifyCanExecuteChanged();
            StopSingleDeviceFakeProxyCommand.NotifyCanExecuteChanged();
        }

        private void NotifyActionPresentationChanged()
        {
            OnPropertyChanged(nameof(CanEditSelectedDeviceConfiguration));
            ViewSingleDeviceRandomizedInfoCommand.NotifyCanExecuteChanged();
            StopDeviceActionCommand.NotifyCanExecuteChanged();
        }

        [RelayCommand(CanExecute = nameof(CanStopDeviceActionCommandCanExecute))]
        private void StopDeviceAction(DeviceRowViewModel? device)
        {
            if (device == null || !device.CanStopAction)
                return;

            _deviceActionCoordinatorService.TryRequestCancellation(device.Serial);
        }

        private static bool CanStopDeviceActionCommandCanExecute(DeviceRowViewModel? device)
        {
            return device?.CanStopAction == true;
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
                    await ShowToolbarLogAsync("Log_ActionCanceled", cancellationToken).ConfigureAwait(true);
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
        private void ToggleSingleDeviceSelection(DeviceRowViewModel? device)
        {
            if (device == null)
                return;

            SelectSingleDevice(ReferenceEquals(SelectedDevice, device) ? null : device);
        }

        [RelayCommand(AllowConcurrentExecutions = true)]
        private async Task DeleteDeviceAsync(DeviceRowViewModel? device)
        {
            if (device == null)
            {
                await ShowToolbarLogAsync("Log_SelectDeviceFirst", CancellationToken.None)
                    .ConfigureAwait(true);
                return;
            }

            DeviceActionEligibilityFailure eligibility = await _deviceActionEligibilityService
                .CheckAsync(
                    device.Serial,
                    DeviceActionRequirement.Idle,
                    CancellationToken.None)
                .ConfigureAwait(true);
            if (eligibility != DeviceActionEligibilityFailure.None)
            {
                _deviceActionFeedbackService.ReportEligibilityFailure(device.Serial, eligibility);
                return;
            }

            string serial = device.Serial;
            string name = device.Name;
            IDeviceActionOperation? operation = TryStartDeviceAction(
                device,
                DeviceActionKind.DeleteDevice,
                canCancel: false);
            if (operation == null)
            {
                ShowBusyActionLog(device);
                return;
            }

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
                        await SetDialogDismissalLogAsync(device, operation);
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
                    await SetOperationCancellationLogAsync(device, operation, requiresOnline: false);
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
            device = await GetContextOnlineDeviceAsync(device, cancellationToken).ConfigureAwait(true);
            if (device == null)
                return;

            SetContextDeviceLog(device, "Log_RebootingDevice");

            try
            {
                await _deviceActionService.RebootAsync(device.Serial, cancellationToken).ConfigureAwait(true);
                SetContextDeviceLog(device, "Log_RebootDeviceSuccess");
            }
            catch (OperationCanceledException)
            {
                await _deviceActionFeedbackService.ReportOperationCanceledAsync(
                        device.Serial,
                        DeviceActionCancellationReason.External,
                        requiresOnline: true,
                        CancellationToken.None)
                    .ConfigureAwait(true);
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Failed to reboot device {Serial}.", device.Serial);
                SetContextDeviceLog(device, "Log_RebootDeviceFailed");
            }
        }

        [RelayCommand]
        private async Task CopySerialAsync(DeviceRowViewModel? device, CancellationToken cancellationToken)
        {
            DeviceRowViewModel? targetDevice = device;
            if (targetDevice == null || string.IsNullOrWhiteSpace(targetDevice.Serial))
                return;

            bool success = false;
            await RunOnUiContextAsync(() =>
            {
                try
                {
                    _clipboardService.SetText(targetDevice.Serial);
                    success = true;
                }
                catch (Exception exception)
                {
                    _logger.LogError(exception, "Failed to copy serial for device {Serial}.", targetDevice.Serial);
                }
            }).ConfigureAwait(true);

            if (success)
            {
                SetContextDeviceLog(targetDevice, "Log_CopySerialSuccess");
            }
            else
            {
                SetContextDeviceLog(targetDevice, "Log_CopySerialFailed");
            }
        }

        [RelayCommand(AllowConcurrentExecutions = true)]
        private Task OpenViewDeviceAsync(DeviceRowViewModel? device)
        {
            if (device == null || string.IsNullOrWhiteSpace(device.Serial))
                return Task.CompletedTask;

            return _viewDeviceWindowService.OpenAsync(device.Serial, device.Name);
        }

        [RelayCommand(AllowConcurrentExecutions = true)]
        private async Task RefreshContextMenuStateAsync(DeviceRowViewModel? device)
        {
            CancellationToken cancellationToken = CancellationToken.None;
            device = await GetContextOnlineDeviceAsync(
                    device,
                    cancellationToken,
                    logOffline: false)
                .ConfigureAwait(true);
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
            device = await GetContextOnlineDeviceAsync(device, cancellationToken).ConfigureAwait(true);
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
                SetContextDeviceLog(device, successLog);
            }
            catch (OperationCanceledException)
            {
                await _deviceActionFeedbackService.ReportOperationCanceledAsync(
                        device.Serial,
                        DeviceActionCancellationReason.External,
                        requiresOnline: true,
                        CancellationToken.None)
                    .ConfigureAwait(true);
            }
            catch (Exception exception)
            {
                _logger.LogError(
                    exception,
                    "Failed to toggle {Package} for device {Serial}.",
                    isGms ? "GMS" : "Play Store",
                    device.Serial);
                SetContextDeviceLog(
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
            device = await GetContextOnlineDeviceAsync(device, cancellationToken)
                .ConfigureAwait(true);
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

                SetContextDeviceLog(device, enabled ? "Log_WifiEnabled" : "Log_WifiDisabled");
            }
            catch (OperationCanceledException)
            {
                await _deviceActionFeedbackService.ReportOperationCanceledAsync(
                        device.Serial,
                        DeviceActionCancellationReason.External,
                        requiresOnline: true,
                        CancellationToken.None)
                    .ConfigureAwait(true);
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Failed to toggle Wi-Fi for device {Serial}.", device.Serial);
                SetContextDeviceLog(device, "Log_WifiToggleFailed");
            }
        }

        [RelayCommand(CanExecute = nameof(CanExecuteSelectedDeviceAction), AllowConcurrentExecutions = true)]
        private async Task RandomizeSingleDeviceInfoAsync()
        {
            DeviceRowViewModel? selectedDevice = GetSingleSelectedDeviceSnapshot();
            if (!await CheckInitialOnlineIdleEligibilityAsync(selectedDevice).ConfigureAwait(true))
                return;

            if (!await RefreshSingleActionConfigurationSnapshotAsync(selectedDevice!.Serial).ConfigureAwait(true))
                return;

            RandomDeviceRequest request = CreateCurrentRandomDeviceRequest();
            IDeviceActionOperation? operation = TryStartEligibleDeviceAction(
                selectedDevice,
                DeviceActionKind.RandomDevice);
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
                    await SetOperationCancellationLogAsync(device, operation, requiresOnline: true);
                }
                catch (Exception exception)
                {
                    _logger.LogError(exception, "Unexpected failure while randomizing device info.");
                    SetDeviceLog(device, "Log_RandomDeviceFailed");
                }
            }
        }

        [RelayCommand(CanExecute = nameof(CanExecuteSelectedDeviceAction), AllowConcurrentExecutions = true)]
        private async Task ChangeAndWipeSingleDeviceAsync()
        {
            DeviceRowViewModel? selectedDevice = GetSingleSelectedDeviceSnapshot();
            if (!await CheckInitialOnlineIdleEligibilityAsync(selectedDevice).ConfigureAwait(true))
                return;

            if (!await RefreshSingleActionConfigurationSnapshotAsync(selectedDevice!.Serial).ConfigureAwait(true))
                return;

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
            IDeviceActionOperation? operation = TryStartEligibleDeviceAction(
                selectedDevice!,
                DeviceActionKind.ChangeDevice);
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
                        await SetDialogDismissalLogAsync(device, operation);
                        return;
                    }

                    if (!await IsOperationTargetOnlineAsync(device, cancellationToken).ConfigureAwait(true))
                        return;

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
                    await SetOperationCancellationLogAsync(device, operation, requiresOnline: true);
                }
                catch (Exception exception)
                {
                    _logger.LogError(exception, "Failed to change device {Serial}.", device.Serial);
                    SetDeviceLog(device, "Log_ChangeDeviceFailed");
                }
            }
        }

        [RelayCommand(CanExecute = nameof(CanExecuteSelectedDeviceAction), AllowConcurrentExecutions = true)]
        private async Task ChangeSingleDeviceWithoutWipeAsync()
        {
            DeviceRowViewModel? selectedDevice = GetSingleSelectedDeviceSnapshot();
            if (!await CheckInitialOnlineIdleEligibilityAsync(selectedDevice).ConfigureAwait(true))
                return;

            if (!await RefreshSingleActionConfigurationSnapshotAsync(selectedDevice!.Serial).ConfigureAwait(true))
                return;

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
            IDeviceActionOperation? operation = TryStartEligibleDeviceAction(
                selectedDevice!,
                DeviceActionKind.ChangeWithoutWipe);
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
                        await SetDialogDismissalLogAsync(device, operation);
                        return;
                    }

                    if (!await IsOperationTargetOnlineAsync(device, cancellationToken).ConfigureAwait(true))
                        return;

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
                    await SetOperationCancellationLogAsync(device, operation, requiresOnline: true);
                }
                catch (Exception exception)
                {
                    _logger.LogError(exception, "Failed to change device {Serial} without wiping data.", device.Serial);
                    SetDeviceLog(device, "Log_ChangeWithoutWipeFailed");
                }
            }
        }

        [RelayCommand(CanExecute = nameof(CanExecuteSelectedDeviceAction), AllowConcurrentExecutions = true)]
        private async Task WipeSingleDeviceWithoutChangeAsync()
        {
            DeviceRowViewModel? selectedDevice = GetSingleSelectedDeviceSnapshot();
            if (!await CheckInitialOnlineIdleEligibilityAsync(selectedDevice).ConfigureAwait(true))
                return;

            if (!await RefreshSingleActionConfigurationSnapshotAsync(selectedDevice!.Serial).ConfigureAwait(true))
                return;

            DeviceChangeOptions changeOptions = CreateCurrentChangeOptions();
            IDeviceActionOperation? operation = TryStartEligibleDeviceAction(
                selectedDevice,
                DeviceActionKind.Wipe);
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
                        await SetDialogDismissalLogAsync(device, operation);
                        return;
                    }

                    if (!await IsOperationTargetOnlineAsync(device, cancellationToken).ConfigureAwait(true))
                        return;

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
                    await SetOperationCancellationLogAsync(device, operation, requiresOnline: true);
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
            return CanExecuteSelectedDeviceAction();
        }

        [RelayCommand(CanExecute = nameof(CanOpenAdvancedChangeConfig))]
        private async Task OpenSingleDeviceAdvancedConfigAsync(CancellationToken cancellationToken)
        {
            DeviceRowViewModel? selectedDevice = GetSingleSelectedDeviceSnapshot();
            if (selectedDevice == null)
                return;

            if (UseDefaultChangeMode)
            {
                _deviceActionFeedbackService.SetNonOwningProcess(
                    selectedDevice.Serial,
                    "Log_AdvancedConfigDefaultModeRequired");
                return;
            }

            if (!await RefreshSingleActionConfigurationSnapshotAsync(selectedDevice.Serial).ConfigureAwait(true))
                return;

            if (UseDefaultChangeMode)
            {
                _deviceActionFeedbackService.SetNonOwningProcess(
                    selectedDevice.Serial,
                    "Log_AdvancedConfigDefaultModeRequired");
                return;
            }

            DeviceProfileConfig profileSnapshot = CreateDeviceProfileConfig();
            DeviceRowViewModel device = selectedDevice;
            DeviceChangeOptions optionsSnapshot =
                DeviceChangeOptionsHelper.CreateNormalizedCopy(profileSnapshot.ChangeOptions);
            _deviceActionFeedbackService.SetNonOwningProcess(device.Serial, "Log_OpeningDialog");
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
                    _deviceActionFeedbackService.ReportNonOwningDialogDismissed(device.Serial);
                    return;
                }

                profileSnapshot.ChangeOptions =
                    DeviceChangeOptionsHelper.CreateNormalizedCopy(result.Options);
                profileSnapshot.UseIntegritySecurityPatch = result.UseIntegritySecurityPatch;
                bool saved = await SaveDeviceProfileAsync(
                        device.Serial,
                        profileSnapshot,
                        cancellationToken)
                    .ConfigureAwait(true);
                if (!saved)
                {
                    _deviceActionFeedbackService.SetNonOwningProcess(
                        device.Serial,
                        "Log_AdvancedChangeConfigFailed");
                    return;
                }

                if (SelectedDevice != null && SerialEquals(SelectedDevice.Serial, device.Serial))
                    ApplyStoredDeviceConfig(SelectedDevice);

                _deviceActionFeedbackService.SetNonOwningProcess(
                    device.Serial,
                    "Log_AdvancedChangeConfigSaved");
            }
            catch (OperationCanceledException)
            {
                await _deviceActionFeedbackService.ReportOperationCanceledAsync(
                        device.Serial,
                        DeviceActionCancellationReason.External,
                        requiresOnline: false,
                        CancellationToken.None)
                    .ConfigureAwait(true);
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Failed to configure advanced Change Device options for {Serial}.", device.Serial);
                _deviceActionFeedbackService.SetNonOwningProcess(
                    device.Serial,
                    "Log_AdvancedChangeConfigFailed");
            }
        }

        [RelayCommand(CanExecute = nameof(CanExecuteSelectedDeviceAction), AllowConcurrentExecutions = true)]
        private async Task RandomizeChangeAndWipeSingleDeviceAsync()
        {
            DeviceRowViewModel? selectedDevice = GetSingleSelectedDeviceSnapshot();
            if (!await CheckInitialOnlineIdleEligibilityAsync(selectedDevice).ConfigureAwait(true))
                return;

            if (!await RefreshSingleActionConfigurationSnapshotAsync(selectedDevice!.Serial).ConfigureAwait(true))
                return;

            DeviceChangeOptions changeOptions = CreateCurrentChangeOptions();
            bool changeSimEnabled = IsChangeSimEnabled;
            RandomDeviceRequest randomRequest = CreateCurrentRandomDeviceRequest();
            IDeviceActionOperation? operation = TryStartEligibleDeviceAction(
                selectedDevice,
                DeviceActionKind.RandomChangeAndWipe);
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
                        await SetDialogDismissalLogAsync(device, operation);
                        return;
                    }

                    DeviceInfoApiDevice? profile;
                    try
                    {
                        SetDeviceLog(device, "Log_RandomAndChangeDevice");
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
                            SetDeviceLog(device, "Log_RandomAndChangeDeviceFailed");
                            return;
                        }

                        profile = randomResult.Profile.Clone();
                        ApplyRandomDeviceInfo(device.Serial, randomResult.Profile.Clone());
                    }
                    catch (OperationCanceledException)
                    {
                        await SetOperationCancellationLogAsync(device, operation, requiresOnline: true);
                        return;
                    }
                    catch (Exception exception)
                    {
                        _logger.LogError(exception, "Unexpected failure while randomizing device info.");
                        SetDeviceLog(device, "Log_RandomAndChangeDeviceFailed");
                        return;
                    }
                    if (!await IsOperationTargetOnlineAsync(device, cancellationToken).ConfigureAwait(true))
                        return;
                    SetDeviceLog(device, "Log_RandomAndChangeDevice");

                    try
                    {
                        IProgress<DeviceChangeStage> progress = CreateDeviceChangeProgress(
                            device,
                            "Log_RandomAndChangeDevice",
                            "Log_RandomAndChangeDeviceSuccess");
                        await _deviceChangeService
                            .ChangeAsync(
                                device.Serial,
                                profile!,
                                changeSimEnabled,
                                changeOptions,
                                progress,
                                cancellationToken)
                            .ConfigureAwait(true);

                        SetDeviceLog(device, "Log_RandomAndChangeDeviceSuccess");
                    }
                    catch (OperationCanceledException)
                    {
                        await SetOperationCancellationLogAsync(device, operation, requiresOnline: true);
                    }
                    catch (Exception exception)
                    {
                        _logger.LogError(exception, "Failed to change device {Serial}.", device.Serial);
                        SetDeviceLog(device, "Log_RandomAndChangeDeviceFailed");
                    }
                }
                catch (OperationCanceledException)
                {
                    await SetOperationCancellationLogAsync(device, operation, requiresOnline: true);
                }
                catch (Exception exception)
                {
                    _logger.LogError(exception, "Failed to randomize and change device {Serial}.", device.Serial);
                    SetDeviceLog(device, "Log_RandomAndChangeDeviceFailed");
                }
            }
        }

        [RelayCommand(CanExecute = nameof(CanExecuteSelectedDeviceAction), AllowConcurrentExecutions = true)]
        private async Task RandomizeSingleDeviceSimInfoAsync()
        {
            DeviceRowViewModel? selectedDevice = GetSingleSelectedDeviceSnapshot();
            if (!await CheckInitialOnlineIdleEligibilityAsync(selectedDevice).ConfigureAwait(true))
                return;

            if (!await RefreshSingleActionConfigurationSnapshotAsync(selectedDevice!.Serial).ConfigureAwait(true))
                return;

            CarrierCountryOption? country = SelectedCountry;
            CarrierOption? carrier = SelectedCarrier;
            IDeviceActionOperation? operation = TryStartEligibleDeviceAction(
                selectedDevice,
                DeviceActionKind.RandomSim);
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
                    await SetOperationCancellationLogAsync(device, operation, requiresOnline: true);
                }
                catch (Exception exception)
                {
                    _logger.LogError(exception, "Failed to generate random SIM information.");
                    SetDeviceLog(device, "Log_RandomSimFailed");
                }
            }
        }

        private async Task<DeviceRowViewModel?> GetContextOnlineDeviceAsync(
            DeviceRowViewModel? device,
            CancellationToken cancellationToken,
            bool logOffline = true)
        {
            if (device == null)
                return null;

            DeviceActionEligibilityFailure failure = await _deviceActionEligibilityService
                .CheckAsync(
                    device.Serial,
                    DeviceActionRequirement.Online,
                    cancellationToken)
                .ConfigureAwait(true);
            ApplyLiveConnectionState(device, failure);
            if (failure == DeviceActionEligibilityFailure.None)
                return device;

            if (logOffline)
                _deviceActionFeedbackService.ReportEligibilityFailure(device.Serial, failure);

            return null;
        }

        private void ApplyLiveConnectionState(
            DeviceRowViewModel device,
            DeviceActionEligibilityFailure result)
        {
            if (result is not (DeviceActionEligibilityFailure.None or DeviceActionEligibilityFailure.Offline))
                return;

            AdbDeviceStatus status = result == DeviceActionEligibilityFailure.None
                ? AdbDeviceStatus.Online
                : AdbDeviceStatus.Offline;
            if (device.ConnectionStatus == status)
                return;

            device.ConnectionStatus = status;
            device.Status = GetConnectionStatusText(status);
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
        private async Task ChangeSingleDeviceSimInfoAsync()
        {
            DeviceRowViewModel? selectedDevice = GetSingleSelectedDeviceSnapshot();
            if (!await CheckInitialOnlineIdleEligibilityAsync(selectedDevice).ConfigureAwait(true))
                return;

            if (!await RefreshSingleActionConfigurationSnapshotAsync(selectedDevice!.Serial).ConfigureAwait(true))
                return;

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

            IDeviceActionOperation? operation = TryStartEligibleDeviceAction(
                selectedDevice!,
                DeviceActionKind.ChangeSim);
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
                        await SetDialogDismissalLogAsync(device, operation);
                        return;
                    }

                    if (!await IsOperationTargetOnlineAsync(device, cancellationToken).ConfigureAwait(true))
                        return;

                    SetDeviceLog(device, "Log_ChangeSim");
                    await _deviceChangeService
                        .ChangeSimAsync(device.Serial, editedProfile!, cancellationToken)
                        .ConfigureAwait(true);
                    ApplyRandomSimInfo(device.Serial, editedProfile!);
                    SetDeviceLog(device, "Log_ChangeSimSuccess");
                }
                catch (OperationCanceledException)
                {
                    await SetOperationCancellationLogAsync(device, operation, requiresOnline: true);
                }
                catch (Exception exception)
                {
                    _logger.LogError(exception, "Failed to change SIM information on device {Serial}.", device.Serial);
                    SetDeviceLog(device, "Log_ChangeSimFailed");
                }
            }
        }

        [RelayCommand(CanExecute = nameof(CanExecuteSelectedDeviceAction), AllowConcurrentExecutions = true)]
        private async Task ChangeSingleDeviceLocationAsync()
        {
            DeviceRowViewModel? selectedDevice = GetSingleSelectedDeviceSnapshot();
            if (!await CheckInitialOnlineIdleEligibilityAsync(selectedDevice).ConfigureAwait(true))
                return;

            if (!await RefreshSingleActionConfigurationSnapshotAsync(selectedDevice!.Serial).ConfigureAwait(true))
                return;

            StoredDeviceConfig? configurationSnapshot =
                CreateStoredDeviceConfigSnapshot(selectedDevice.Serial);
            IDeviceActionOperation? operation = TryStartEligibleDeviceAction(
                selectedDevice,
                DeviceActionKind.ChangeLocation);
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
                        .ShowChangeLocationAsync(
                            device.Serial,
                            device.Name,
                            configurationSnapshot,
                            cancellationToken)
                        .ConfigureAwait(true);

                    if (dialogResult == null)
                    {
                        await SetDialogDismissalLogAsync(device, operation);
                        return;
                    }


                    if (!await IsOperationTargetOnlineAsync(device, cancellationToken).ConfigureAwait(true))
                        return;

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
                    await SetOperationCancellationLogAsync(device, operation, requiresOnline: true);
                }
                catch (Exception exception)
                {
                    _logger.LogError(exception, "Failed to change location for device {Serial}.", device.Serial);
                    SetDeviceLog(device, "Log_ChangeLocationFailed");
                }
                finally
                {
                    await SynchronizeStoredDeviceCacheAsync(device.Serial).ConfigureAwait(true);
                }
            }
        }

        [RelayCommand(CanExecute = nameof(CanExecuteSelectedDeviceAction), AllowConcurrentExecutions = true)]
        private async Task ChangeSingleDeviceTimezoneAsync()
        {
            DeviceRowViewModel? selectedDevice = GetSingleSelectedDeviceSnapshot();
            if (!await CheckInitialOnlineIdleEligibilityAsync(selectedDevice).ConfigureAwait(true))
                return;

            if (!await RefreshSingleActionConfigurationSnapshotAsync(selectedDevice!.Serial).ConfigureAwait(true))
                return;

            StoredDeviceConfig? configurationSnapshot =
                CreateStoredDeviceConfigSnapshot(selectedDevice.Serial);
            IDeviceActionOperation? operation = TryStartEligibleDeviceAction(
                selectedDevice,
                DeviceActionKind.ChangeTimezone);
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
                        .ShowChangeTimezoneAsync(
                            device.Serial,
                            device.Name,
                            configurationSnapshot,
                            cancellationToken)
                        .ConfigureAwait(true);

                    if (dialogResult == null)
                    {
                        await SetDialogDismissalLogAsync(device, operation);
                        return;
                    }


                    if (!await IsOperationTargetOnlineAsync(device, cancellationToken).ConfigureAwait(true))
                        return;

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
                    await SetOperationCancellationLogAsync(device, operation, requiresOnline: true);
                }
                catch (Exception exception)
                {
                    _logger.LogError(exception, "Failed to change timezone for device {Serial}.", device.Serial);
                    SetDeviceLog(device, "Log_ChangeTimezoneFailed");
                }
                finally
                {
                    await SynchronizeStoredDeviceCacheAsync(device.Serial).ConfigureAwait(true);
                }
            }
        }

        private async Task SaveUpdateIntegrityConfigAsync(
            DeviceRowViewModel deviceRow,
            UpdateIntegrityDialogResult result,
            CancellationToken cancellationToken)
        {
            await _deviceRefreshLock.WaitAsync(cancellationToken).ConfigureAwait(true);
            try
            {
                bool saved = await _deviceConfigService
                    .SaveUpdateIntegrityConfigAsync(_storedDevices, deviceRow.Serial, result, cancellationToken)
                    .ConfigureAwait(true);
                if (!saved)
                    throw new InvalidOperationException("The Update Integrity configuration could not be saved.");
            }
            finally
            {
                _deviceRefreshLock.Release();
            }
        }

        private bool CanViewRandomDeviceInfo()
        {
            DeviceRowViewModel? selectedDevice = GetSingleSelectedDeviceSnapshot();
            return selectedDevice != null
                && !IsDeviceBusy(selectedDevice)
                && _randomDeviceProfiles.ContainsKey(selectedDevice.Serial);
        }

        [RelayCommand(CanExecute = nameof(CanViewRandomDeviceInfo), AllowConcurrentExecutions = true)]
        private async Task ViewSingleDeviceRandomizedInfoAsync()
        {
            CancellationToken cancellationToken = CancellationToken.None;
            DeviceRowViewModel? device = GetSingleSelectedDeviceSnapshot();
            if (device == null)
                return;

            DeviceActionEligibilityFailure eligibility = await _deviceActionEligibilityService
                .CheckAsync(
                    device.Serial,
                    DeviceActionRequirement.Idle,
                    cancellationToken)
                .ConfigureAwait(true);
            if (eligibility != DeviceActionEligibilityFailure.None)
            {
                _deviceActionFeedbackService.ReportEligibilityFailure(device.Serial, eligibility);
                return;
            }

            if (!_randomDeviceProfiles.TryGetValue(device.Serial, out DeviceInfoApiDevice? profile))
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
                    await SetOperationCancellationLogAsync(device, operation, requiresOnline: false);
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
                bool hasLocationMetadata =
                    !string.IsNullOrWhiteSpace(countryCode)
                    || !string.IsNullOrWhiteSpace(cityName);
                bool saved = hasLocationMetadata
                    ? await _deviceConfigService
                        .SaveLocationConfigAsync(
                            _storedDevices,
                            serial,
                            mode,
                            latitude,
                            longitude,
                            countryCode,
                            cityName,
                            cancellationToken)
                        .ConfigureAwait(true)
                    : await _deviceConfigService
                        .SaveLocationConfigAsync(
                            _storedDevices,
                            serial,
                            mode,
                            latitude,
                            longitude,
                            cancellationToken)
                        .ConfigureAwait(true);
                if (!saved)
                    throw new InvalidOperationException("The Location configuration could not be saved.");
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
                bool saved = await _deviceConfigService
                    .SaveTimezoneConfigAsync(
                        _storedDevices,
                        serial,
                        mode,
                        timezone,
                        cancellationToken)
                    .ConfigureAwait(true);
                if (!saved)
                    throw new InvalidOperationException("The Timezone configuration could not be saved.");
            }
            finally
            {
                _deviceRefreshLock.Release();
            }
        }

        [RelayCommand(CanExecute = nameof(CanExecuteSelectedDeviceAction), AllowConcurrentExecutions = true)]
        private async Task UpdateSingleDeviceIntegrityAsync()
        {
            DeviceRowViewModel? selectedDevice = GetSingleSelectedDeviceSnapshot();
            if (!await CheckInitialOnlineIdleEligibilityAsync(selectedDevice).ConfigureAwait(true))
                return;

            if (!await RefreshSingleActionConfigurationSnapshotAsync(selectedDevice!.Serial).ConfigureAwait(true))
                return;

            StoredDeviceConfig? storedDeviceSnapshot =
                CreateStoredDeviceConfigSnapshot(selectedDevice.Serial);
            if (storedDeviceSnapshot == null)
            {
                SetDeviceLog(selectedDevice, "Log_UpdateIntegrityFailed");
                return;
            }

            IDeviceActionOperation? operation = TryStartEligibleDeviceAction(
                selectedDevice,
                DeviceActionKind.UpdateIntegrity);
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
                            (result, saveCancellationToken) => SaveUpdateIntegrityConfigAsync(
                                device,
                                result,
                                saveCancellationToken),
                            cancellationToken)
                        .ConfigureAwait(true);

                    if (dialogResult == null)
                    {
                        await SetDialogDismissalLogAsync(device, operation);
                        return;
                    }

                    await SaveUpdateIntegrityConfigAsync(
                            device,
                            dialogResult,
                            cancellationToken)
                        .ConfigureAwait(true);

                    if (!await IsOperationTargetOnlineAsync(device, cancellationToken).ConfigureAwait(true))
                        return;

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
                    await SetOperationCancellationLogAsync(device, operation, requiresOnline: true);
                }
                catch (Exception exception)
                {
                    _logger.LogError(exception, "Failed to update integrity for device {Serial}.", device.Serial);
                    SetDeviceLog(device, "Log_UpdateIntegrityFailed");
                }
            }
        }

        [RelayCommand(CanExecute = nameof(CanExecuteSelectedDeviceAction), AllowConcurrentExecutions = true)]
        private async Task InstallPackagesOnSingleDeviceAsync()
        {
            DeviceRowViewModel? selectedDevice = GetSingleSelectedDeviceSnapshot();
            if (!await CheckInitialOnlineIdleEligibilityAsync(selectedDevice).ConfigureAwait(true))
                return;

            IDeviceActionOperation? operation = TryStartEligibleDeviceAction(
                selectedDevice!,
                DeviceActionKind.InstallPackages);
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
                        await SetDialogDismissalLogAsync(device, operation);
                        return;
                    }

                    var request = new InstallPackageRequest(
                        dialogResult.FilePaths.ToArray(),
                        new InstallPackageOptions(
                            dialogResult.Options.GrantPermissions,
                            dialogResult.Options.AllowDowngrade));

                    if (!await IsOperationTargetOnlineAsync(device, cancellationToken).ConfigureAwait(true))
                        return;

                    SetDeviceLog(device, "Log_InstallPackageInstalling");
                    InstallPackageSetResult result = await _packageInstallService
                        .InstallManyAsync(
                            device.Serial,
                            request.FilePaths,
                            request.Options,
                            cancellationToken)
                        .ConfigureAwait(true);
                    SetDeviceLog(
                        device,
                        result.MessageResourceKey,
                        result.MessageArguments.ToArray());

                }
                catch (OperationCanceledException)
                {
                    await SetOperationCancellationLogAsync(device, operation, requiresOnline: true);
                }
                catch (Exception exception)
                {
                    _logger.LogError(exception, "Failed to install package for selected device.");
                    SetDeviceLog(device, "Log_InstallPackageAdbFailure");
                }
            }
        }

        [RelayCommand(CanExecute = nameof(CanExecuteSelectedDeviceAction), AllowConcurrentExecutions = true)]
        private async Task StartSingleDeviceFakeProxyAsync()
        {
            DeviceRowViewModel? selectedDevice = GetSingleSelectedDeviceSnapshot();
            if (!await CheckInitialOnlineIdleEligibilityAsync(selectedDevice).ConfigureAwait(true))
                return;

            if (!await RefreshSingleActionConfigurationSnapshotAsync(selectedDevice!.Serial).ConfigureAwait(true))
                return;

            StoredDeviceConfig? configurationSnapshot =
                CreateStoredDeviceConfigSnapshot(selectedDevice.Serial);
            IDeviceActionOperation? operation = TryStartEligibleDeviceAction(
                selectedDevice,
                DeviceActionKind.FakeProxy);
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
                        .ShowFakeProxyDialogAsync(
                            device.Serial,
                            device.Name,
                            configurationSnapshot,
                            cancellationToken)
                        .ConfigureAwait(true);

                    if (dialogResult == null)
                    {
                        await SetDialogDismissalLogAsync(device, operation);
                        return;
                    }

                    if (!await IsOperationTargetOnlineAsync(device, cancellationToken).ConfigureAwait(true))
                        return;

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

                    SetDeviceLog(
                        device,
                        workflowResult.IsSuccess
                            ? "Log_FakeProxySuccess"
                            : "Log_FakeProxyPartialSuccess");
                }
                catch (OperationCanceledException)
                {
                    await SetOperationCancellationLogAsync(device, operation, requiresOnline: true);
                }
                catch (Exception exception)
                {
                    _logger.LogError(exception, "Failed to apply fake proxy for device {Serial}.", device.Serial);
                    SetDeviceLog(device, "Log_FakeProxyFailed");
                }
                finally
                {
                    await SynchronizeStoredDeviceCacheAsync(device.Serial).ConfigureAwait(true);
                }
            }
        }

        [RelayCommand(CanExecute = nameof(CanExecuteSelectedDeviceAction), AllowConcurrentExecutions = true)]
        private async Task StopSingleDeviceFakeProxyAsync()
        {
            DeviceRowViewModel? selectedDevice = GetSingleSelectedDeviceSnapshot();
            if (!await CheckInitialOnlineIdleEligibilityAsync(selectedDevice).ConfigureAwait(true))
                return;

            IDeviceActionOperation? operation = TryStartEligibleDeviceAction(
                selectedDevice!,
                DeviceActionKind.StopFakeProxy);
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
                    await SetOperationCancellationLogAsync(device, operation, requiresOnline: true);
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
                IReadOnlyList<AdbDevice> connectedDevices = await _deviceListService
                    .LoadDetectedDevicesAsync(cancellationToken)
                    .ConfigureAwait(false);
                int newDeviceCount = _deviceListService.CountNewDevices(_storedDevices, connectedDevices);
                await RunOnUiContextAsync(() =>
                {
                    UpdateDeviceConnectionStatuses(connectedDevices);
                    if (!_isShowingToolbarLog)
                    {
                        NewDeviceCountText = string.Format(
                            _localizationService.GetString("ChangeSingleDevice_NewDeviceCount"),
                            newDeviceCount);
                    }
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
                    if (!_isShowingToolbarLog)
                    {
                        NewDeviceCountText = string.Format(
                            _localizationService.GetString("ChangeSingleDevice_NewDeviceCount"),
                            _localizationService.GetString("ChangeSingleDevice_NotAvailable"));
                    }
                }).ConfigureAwait(false);
            }
            finally
            {
                _deviceRefreshLock.Release();
            }
        }

        private async Task SynchronizeStoredDeviceCacheAsync(string serial)
        {
            await _deviceRefreshLock.WaitAsync(CancellationToken.None).ConfigureAwait(false);
            try
            {
                IReadOnlyList<StoredDeviceConfig> storedDevices =
                    await _deviceListService.LoadStoredDevicesAsync(CancellationToken.None)
                        .ConfigureAwait(false);
                await RunOnUiContextAsync(() =>
                {
                    _storedDevices = storedDevices.ToList();
                    if (SelectedDevice != null && SerialEquals(SelectedDevice.Serial, serial))
                        ApplyStoredDeviceConfig(SelectedDevice);
                }).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                _logger.LogWarning(
                    exception,
                    "Failed to synchronize stored configuration after a device dialog for {Serial}.",
                    serial);
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
            return DeviceRowFilterHelper.Matches(device, SelectedDeviceFilter, DeviceSearchText);
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
                ApplyDeviceFilter();
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

                _selectedDevices.Clear();
                if (selectedDevice != null)
                    _selectedDevices.Add(selectedDevice);

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
                NotifySelectionChanged();
            }
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

            ViewSingleDeviceRandomizedInfoCommand.NotifyCanExecuteChanged();
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
                includeSelectedCarrierConfig ? SelectedCountry : null,
                includeSelectedCarrierConfig ? SelectedCarrier : null);
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
                var selectedCountry = DeviceProfileOptionsHelper.FindCountryByIso(
                        Countries,
                        storedDevice?.CountryIso)
                    ?? DeviceProfileOptionsHelper.FindCountryByName(
                        Countries,
                        storedDevice?.CountryName)
                    ?? DeviceProfileOptionsHelper.FindCountryByIso(
                        Countries,
                        DefaultCountryIso)
                    ?? Countries.FirstOrDefault();

                SelectedBrand =
                    DeviceProfileOptionsHelper.FindOption(Brands, storedDevice?.Brand) ?? "Random";
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

        private void UpdateAndroidVersionOptions(string? brand, string? preferredVersion)
        {
            AndroidVersions.Clear();
            foreach (string version in DeviceProfileOptionsHelper.GetAndroidVersions(brand))
                AndroidVersions.Add(version);

            SelectedAndroidVersion =
                DeviceProfileOptionsHelper.FindOption(AndroidVersions, preferredVersion)
                ?? "Random";
        }

        private void QueueSelectedDeviceProfileSave()
        {
            DeviceRowViewModel? selectedDevice = GetSingleSelectedDeviceSnapshot();
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
            _ = await SaveDeviceProfileAsync(pendingEdit.Serial, pendingEdit.Profile, cancellationToken)
                .ConfigureAwait(false);
        }

        private async Task<bool> SaveDeviceProfileAsync(
            string serial,
            DeviceProfileConfig profile,
            CancellationToken cancellationToken)
        {
            try
            {
                await _deviceRefreshLock.WaitAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    bool saved = await _deviceConfigService
                        .SaveDeviceProfileAsync(
                            _storedDevices,
                            serial,
                            profile,
                            cancellationToken)
                        .ConfigureAwait(false);
                    if (!saved)
                    {
                        _logger.LogWarning(
                            "Device profile configuration was not saved for {Serial}.",
                            serial);
                    }

                    return saved;
                }
                finally
                {
                    _deviceRefreshLock.Release();
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Failed to save device profile config.");
                return false;
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
            deviceRow.RestoreAction(_deviceActionCoordinatorService.GetOperation(deviceRow.Serial));
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
            SimProfile? simProfile = SimProfileHelper.FromDeviceProfile(randomDevice);
            if (simProfile == null)
                _randomSimProfiles.Remove(serial);
            else
                _randomSimProfiles[serial] = simProfile;

            ViewSingleDeviceRandomizedInfoCommand.NotifyCanExecuteChanged();
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
            DeviceRowViewModel? selectedDevice = GetSingleSelectedDeviceSnapshot();
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
                Country = SelectedCountry,
                Carrier = SelectedCarrier
            };
        }

        private StoredDeviceConfig? CreateStoredDeviceConfigSnapshot(string serial)
        {
            return CreateStoredDeviceConfigSnapshot(_storedDevices, serial);
        }

        private static StoredDeviceConfig? CreateStoredDeviceConfigSnapshot(
            IReadOnlyList<StoredDeviceConfig> storedDevices,
            string serial)
        {
            StoredDeviceConfig? storedDevice = storedDevices
                .FirstOrDefault(device => SerialEquals(device.Serial, serial));
            return storedDevice == null
                ? null
                : StoredDeviceConfigSnapshot.Create(storedDevice);
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
