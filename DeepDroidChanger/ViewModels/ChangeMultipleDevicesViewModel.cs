using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DeepDroidChanger.Helpers;
using DeepDroidChanger.Models;
using DeepDroidChanger.Services;
using Microsoft.Extensions.Logging;

namespace DeepDroidChanger.ViewModels;

public sealed partial class ChangeMultipleDevicesViewModel : ObservableObject, IDisposable
{
    private const int NewDevicePollSeconds = 3;
    private const int SaveDebounceMilliseconds = 300;
    private const int MaxConcurrentBatchActions = 4;
    private const string DefaultCountryIso = "us";
    private static readonly TimeSpan BusyActionLogDuration = TimeSpan.FromSeconds(3);

    private readonly IAddDevicesDialogService _addDevicesDialogService;
    private readonly IAdvancedChangeConfigDialogService _advancedChangeConfigDialogService;
    private readonly ICarrierDataService _carrierDataService;
    private readonly IDeviceActionConfirmationDialogService _deviceActionConfirmationDialogService;
    private readonly IDeviceActionService _deviceActionService;
    private readonly IDeviceChangeService _deviceChangeService;
    private readonly IDeviceConfigService _deviceConfigService;
    private readonly IDeviceLocationService _deviceLocationService;
    private readonly IDeviceListService _deviceListService;
    private readonly IDeviceActionCoordinatorService _deviceActionCoordinatorService;
    private readonly IDeviceProcessStateService _deviceProcessStateService;
    private readonly IDeviceTimezoneService _deviceTimezoneService;
    private readonly IChangeLocationDialogService _changeLocationDialogService;
    private readonly IChangeTimezoneDialogService _changeTimezoneDialogService;
    private readonly IInstallPackageDialogService _installPackageDialogService;
    private readonly IPackageInstallService _packageInstallService;
    private readonly ILocalizationService _localizationService;
    private readonly IMultipleDeviceConfigService _multipleDeviceConfigService;
    private readonly IRandomDeviceInfoDialogService _randomDeviceInfoDialogService;
    private readonly IRandomDeviceService _randomDeviceService;
    private readonly ISimProfileService _simProfileService;
    private readonly ISettingsService _settingsService;
    private readonly IUiDispatcherService _uiDispatcher;
    private readonly IPollingService _pollingService;
    private readonly AppSettings _settings;
    private readonly ILogger<ChangeMultipleDevicesViewModel> _logger;
    private readonly SemaphoreSlim _deviceRefreshLock = new(1, 1);
    private readonly SemaphoreSlim _lifecycleLock = new(1, 1);
    private readonly SemaphoreSlim _batchActionThrottle =
        new(MaxConcurrentBatchActions, MaxConcurrentBatchActions);
    private readonly object _activeBatchTargetsLock = new();
    private readonly HashSet<BatchActionTarget> _activeBatchTargets = [];
    private readonly object _pendingDeviceEditsLock = new();
    private readonly Dictionary<string, PendingDeviceEdit> _pendingDeviceEdits =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly object _pendingConfigSaveLock = new();
    private readonly object _pendingSettingsSaveLock = new();
    private readonly object _batchWorkflowsLock = new();
    private readonly Dictionary<Task, CancellationTokenSource> _batchWorkflows = [];
    private readonly object _activeContextOperationsLock = new();
    private readonly HashSet<Guid> _activeContextOperationIds = [];
    private TaskCompletionSource? _activeContextOperationsCompletion;
    private readonly List<DeviceRowViewModel> _allDeviceRows = [];
    private readonly Dictionary<string, DeviceInfoApiDevice> _randomDeviceProfiles =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, SimProfile> _randomSimProfiles =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<Guid, int> _runningActionOrder = [];
    private int _nextRunningActionOrder;
    private List<StoredDeviceConfig> _storedDevices = [];
    private List<CarrierProfile> _carrierProfiles = [];
    private DeviceChangeOptions _changeOptions = new();
    private CancellationTokenSource? _pollCancellation;
    private Task? _pollTask;
    private CancellationTokenSource? _configSaveCancellation;
    private Task _configSaveTask = Task.CompletedTask;
    private CancellationTokenSource? _settingsSaveCancellation;
    private Task _settingsSaveTask = Task.CompletedTask;
    private CancellationTokenSource _actionLifetimeCancellation = new();
    private bool _isRefreshingRows;
    private bool _isApplyingConfiguration;
    private bool _isUpdatingCarrierOptions;
    private bool _isBatchUpdatingSelection;
    private bool _isSynchronizingDeviceInfo;
    private bool _isDisposed;

    [ObservableProperty]
    private bool _isLoadingDevices;

    [ObservableProperty]
    private string _newDeviceCountText = string.Empty;

    [ObservableProperty]
    private string _selectedDeviceFilter = "All";

    [ObservableProperty]
    private string _deviceSearchText = string.Empty;

    [ObservableProperty]
    private string? _selectedBrand;

    [ObservableProperty]
    private string? _selectedAndroidVersion;

    [ObservableProperty]
    private string _selectedModel = string.Empty;

    [ObservableProperty]
    private CarrierCountryOption? _selectedCountry;

    [ObservableProperty]
    private CarrierOption? _selectedCarrier;

    [ObservableProperty]
    private bool _isChangeSimEnabled = true;

    [ObservableProperty]
    private bool _useIntegritySecurityPatch = true;

    [ObservableProperty]
    private bool _useDefaultChangeMode = true;

    private DeviceRowViewModel? _selectedInfoDevice;

    public ChangeMultipleDevicesViewModel(
        IAddDevicesDialogService addDevicesDialogService,
        IAdvancedChangeConfigDialogService advancedChangeConfigDialogService,
        ICarrierDataService carrierDataService,
        IDeviceActionConfirmationDialogService deviceActionConfirmationDialogService,
        IDeviceChangeService deviceChangeService,
        IDeviceConfigService deviceConfigService,
        IDeviceListService deviceListService,
        IDeviceActionCoordinatorService deviceActionCoordinatorService,
        IDeviceProcessStateService deviceProcessStateService,
        ILocalizationService localizationService,
        IMultipleDeviceConfigService multipleDeviceConfigService,
        IRandomDeviceInfoDialogService randomDeviceInfoDialogService,
        IRandomDeviceService randomDeviceService,
        ISimProfileService simProfileService,
        ISettingsService settingsService,
        IUiDispatcherService uiDispatcher,
        IPollingService pollingService,
        AppSettings settings,
        ILogger<ChangeMultipleDevicesViewModel> logger,
        IDeviceActionService deviceActionService,
        IDeviceLocationService deviceLocationService,
        IDeviceTimezoneService deviceTimezoneService,
        IChangeLocationDialogService changeLocationDialogService,
        IChangeTimezoneDialogService changeTimezoneDialogService,
        IInstallPackageDialogService installPackageDialogService,
        IPackageInstallService packageInstallService)
    {
        _addDevicesDialogService = addDevicesDialogService;
        _advancedChangeConfigDialogService = advancedChangeConfigDialogService;
        _carrierDataService = carrierDataService;
        _deviceActionConfirmationDialogService = deviceActionConfirmationDialogService;
        _deviceActionService = deviceActionService;
        _deviceChangeService = deviceChangeService;
        _deviceConfigService = deviceConfigService;
        _deviceLocationService = deviceLocationService;
        _deviceListService = deviceListService;
        _deviceActionCoordinatorService = deviceActionCoordinatorService;
        _deviceProcessStateService = deviceProcessStateService;
        _deviceTimezoneService = deviceTimezoneService;
        _changeLocationDialogService = changeLocationDialogService;
        _changeTimezoneDialogService = changeTimezoneDialogService;
        _installPackageDialogService = installPackageDialogService;
        _packageInstallService = packageInstallService;
        _localizationService = localizationService;
        _multipleDeviceConfigService = multipleDeviceConfigService;
        _randomDeviceInfoDialogService = randomDeviceInfoDialogService;
        _randomDeviceService = randomDeviceService;
        _simProfileService = simProfileService;
        _settingsService = settingsService;
        _uiDispatcher = uiDispatcher;
        _pollingService = pollingService;
        _settings = settings;
        _logger = logger;

        Devices = [];
        SelectedDevices = [];
        Countries = [];
        Carriers = [];
        AndroidVersions = [];
        Brands = DeviceProfileOptionsHelper.Brands;
        TypeOptions = ["sargo", "starlte", "tissot", "unknown"];
        _isApplyingConfiguration = true;
        try
        {
            UpdateAndroidVersionOptions("Random", null);
            SelectedBrand = Brands.FirstOrDefault();
            SelectedAndroidVersion = AndroidVersions.FirstOrDefault();
        }
        finally
        {
            _isApplyingConfiguration = false;
        }

        NewDeviceCountText = FormatCount("ChangeMultipleDevices_NewDeviceCount", 0);
        DeviceInfo.PropertyChanged += OnDeviceInfoPropertyChanged;
        _deviceActionCoordinatorService.OperationStateChanged += OnDeviceActionStateChanged;
        _deviceProcessStateService.ProcessChanged += OnDeviceProcessChanged;
        RefreshRunningActions();
    }

    public ObservableCollection<DeviceRowViewModel> Devices { get; }
    public ObservableCollection<DeviceRowViewModel> SelectedDevices { get; }
    public ObservableCollection<RunningActionItemViewModel> RunningActions { get; } = [];
    public DeviceInfoFormViewModel DeviceInfo { get; } = CreateDefaultDeviceInfo();
    public ObservableCollection<CarrierCountryOption> Countries { get; }
    public ObservableCollection<CarrierOption> Carriers { get; }
    public ObservableCollection<string> AndroidVersions { get; }
    public IReadOnlyList<string> Brands { get; }
    public IReadOnlyList<string> TypeOptions { get; }
    public IReadOnlyDictionary<string, double> DeviceTableColumnRatios =>
        _settings.DeviceTableColumnRatios;

    public DeviceRowViewModel? SelectedInfoDevice
    {
        get => _selectedInfoDevice;
        set
        {
            if (!SetProperty(ref _selectedInfoDevice, value))
                return;

            ApplySelectedDeviceInfo(value);
            OnPropertyChanged(nameof(CanInteractWithSelectedInfoDevice));
            OnPropertyChanged(nameof(CanOperateSelectedInfoDevice));
            OnPropertyChanged(nameof(DisplayedSelectedDeviceActionKind));
            OnPropertyChanged(nameof(HasExternalSelectedDeviceAction));
            OnPropertyChanged(nameof(ExternalSelectedDeviceActionText));
            NotifyBatchPresentationChanged();
            NotifyBatchActionCanExecuteChanged();
            ViewRandomDeviceInfoCommand.NotifyCanExecuteChanged();
        }
    }

    public bool CanInteractWithSelectedInfoDevice =>
        SelectedInfoDevice == null || !IsDeviceBusy(SelectedInfoDevice);

    public bool CanOperateSelectedInfoDevice =>
        SelectedInfoDevice != null && !IsDeviceBusy(SelectedInfoDevice);

    public DeviceActionKind? DisplayedSelectedDeviceActionKind =>
        GetSelectedInfoDeviceOperation()?.Kind.ToLogicalActionKind();

    public bool HasExternalSelectedDeviceAction =>
        GetSelectedInfoDeviceOperation() is { } operation && !operation.Kind.IsBatchAction();

    public string ExternalSelectedDeviceActionText =>
        GetExternalSelectedDeviceActionText();

    public DeviceActionKind? SelectedBatchActionKind =>
        GetSelectedInfoDeviceBatchOperation()?.Kind;

    public bool IsSelectedInfoDeviceActiveBatchTarget =>
        GetSelectedInfoDeviceBatchOperation() != null;

    public bool HasSelectedInfoDeviceBatchStopButton =>
        GetSelectedInfoDeviceBatchOperation() is { State: not DeviceActionRuntimeState.Idle };

    public bool ShowSelectedDeviceBatchStop =>
        HasSelectedInfoDeviceBatchStopButton;

    public bool HasActiveBatchActionButton => ShowSelectedDeviceBatchStop;

    public bool CanStopSelectedDeviceAction =>
        GetSelectedInfoDeviceBatchOperation() is
        {
            State: DeviceActionRuntimeState.Running,
            CanCancel: true
        };

    public int SelectedBatchActionButtonRow => SelectedBatchActionKind switch
    {
        DeviceActionKind.BatchRandomDevice => 0,
        DeviceActionKind.BatchChangeDevice => 0,
        DeviceActionKind.BatchWipe => 1,
        DeviceActionKind.BatchChangeWithoutWipe => 1,
        DeviceActionKind.BatchRandomSim => 2,
        DeviceActionKind.BatchChangeSim => 2,
        DeviceActionKind.BatchRandomChangeAndWipe => 3,
        DeviceActionKind.BatchInstallPackages => 3,
        DeviceActionKind.BatchChangeLocation => 4,
        DeviceActionKind.BatchChangeTimezone => 4,
        _ => 0
    };

    public int SelectedBatchActionButtonColumn => SelectedBatchActionKind switch
    {
        DeviceActionKind.BatchChangeDevice
            or DeviceActionKind.BatchChangeWithoutWipe
            or DeviceActionKind.BatchChangeSim
            or DeviceActionKind.BatchInstallPackages
            or DeviceActionKind.BatchChangeTimezone => 1,
        _ => 0
    };

    public string BatchActionStopText =>
        _localizationService.GetString(
            GetSelectedInfoDeviceBatchOperation()?.State == DeviceActionRuntimeState.Stopping
            ? "ChangeMultipleDevices_StoppingAction"
            : "ChangeMultipleDevices_StopAction");

    public bool? AllDevicesSelectionState
    {
        get
        {
            if (_allDeviceRows.Count == 0)
                return false;

            int selectedCount = _allDeviceRows.Count(device => device.IsSelected);
            if (selectedCount == 0)
                return false;

            return selectedCount == _allDeviceRows.Count ? true : null;
        }
    }

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
            await LoadConfigurationAsync(cancellationToken).ConfigureAwait(false);
            await LoadDevicesAsync(cancellationToken).ConfigureAwait(false);
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
        await CancelBatchWorkflowsAsync().ConfigureAwait(false);
        Task activeContextOperations;
        lock (_activeContextOperationsLock)
            activeContextOperations = _activeContextOperationsCompletion?.Task ?? Task.CompletedTask;
        await activeContextOperations.ConfigureAwait(false);
        await SuspendAsync().ConfigureAwait(false);
    }

    public async Task SuspendAsync()
    {
        await _lifecycleLock.WaitAsync().ConfigureAwait(false);
        try
        {
            await StopPollingAsync().ConfigureAwait(false);
            await FlushPendingDeviceEditsAsync().ConfigureAwait(false);
            await FlushPendingConfigurationSaveAsync().ConfigureAwait(false);
            await FlushPendingSettingsSaveAsync().ConfigureAwait(false);
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
            _logger.LogError(exception, "Multiple Devices polling failed while suspending the view.");
        }
        finally
        {
            _pollTask = null;
            _pollCancellation = null;
            cancellation?.Dispose();
        }
    }

    partial void OnSelectedDeviceFilterChanged(string value)
    {
        ApplyDeviceFilter();
    }

    partial void OnSelectedCountryChanged(CarrierCountryOption? value)
    {
        if (_isApplyingConfiguration)
            return;

        UpdateCarrierOptionsForCountry(value?.CountryIso, null);
        QueueConfigurationSave();
    }

    partial void OnSelectedCarrierChanged(CarrierOption? value)
    {
        if (!_isApplyingConfiguration && !_isUpdatingCarrierOptions)
            QueueConfigurationSave();
    }

    partial void OnSelectedBrandChanged(string? value)
    {
        UpdateAndroidVersionOptions(value, SelectedAndroidVersion);
        if (!_isApplyingConfiguration)
            QueueConfigurationSave();
    }

    partial void OnSelectedAndroidVersionChanged(string? value)
    {
        if (!_isApplyingConfiguration)
            QueueConfigurationSave();
    }

    partial void OnSelectedModelChanged(string value)
    {
        if (!_isApplyingConfiguration)
            QueueConfigurationSave();
    }

    partial void OnIsChangeSimEnabledChanged(bool value)
    {
        if (!_isApplyingConfiguration)
            QueueConfigurationSave();
    }

    partial void OnUseIntegritySecurityPatchChanged(bool value)
    {
        if (!_isApplyingConfiguration)
            QueueConfigurationSave();
    }

    partial void OnUseDefaultChangeModeChanged(bool value)
    {
        _changeOptions.UseDefaultMode = value;
        OpenAdvancedChangeConfigCommand.NotifyCanExecuteChanged();
        if (!_isApplyingConfiguration)
            QueueConfigurationSave();
    }

    [RelayCommand(CanExecute = nameof(CanAddNewDevices))]
    private async Task AddNewDevicesAsync(CancellationToken cancellationToken)
    {
        IsLoadingDevices = true;
        try
        {
            IReadOnlyList<StoredDeviceConfig> selectedDevices =
                await _addDevicesDialogService
                    .ShowAddDevicesAsync(cancellationToken)
                    .ConfigureAwait(true);
            if (selectedDevices.Count == 0)
                return;

            await _deviceRefreshLock.WaitAsync(cancellationToken).ConfigureAwait(true);
            try
            {
                DeviceListSnapshot snapshot = await _deviceListService
                    .AddSelectedDevicesAsync(selectedDevices, cancellationToken)
                    .ConfigureAwait(true);
                ApplyDeviceListSnapshot(snapshot);
            }
            finally
            {
                _deviceRefreshLock.Release();
            }

            await RefreshNewDeviceCountAsync(cancellationToken).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Failed to add devices from Multiple Device screen.");
        }
        finally
        {
            IsLoadingDevices = false;
        }
    }

    private bool CanAddNewDevices()
    {
        return !IsLoadingDevices;
    }

    private bool CanExecuteContextDeviceAction(DeviceRowViewModel? device)
    {
        return device == null || !IsDeviceBusy(device);
    }

    [RelayCommand]
    private void ToggleDeviceSelection(DeviceRowViewModel? device)
    {
        if (device == null)
            return;

        device.IsSelected = !device.IsSelected;
    }

    [RelayCommand]
    private void ToggleSelectAllDevices()
    {
        if (_allDeviceRows.Count == 0)
            return;

        bool shouldSelect = _allDeviceRows.Any(device => !device.IsSelected);
        _isBatchUpdatingSelection = true;
        try
        {
            foreach (DeviceRowViewModel device in _allDeviceRows)
                device.IsSelected = shouldSelect;
        }
        finally
        {
            _isBatchUpdatingSelection = false;
        }

        SynchronizeSelectedDeviceSettings();
    }

    private bool CanRunSelectedDeviceBatchAction()
    {
        return _allDeviceRows.Any(device =>
            device.IsSelected
            && (device.ConnectionStatus == AdbDeviceStatus.Online || IsDeviceBusy(device)));
    }

    [RelayCommand(CanExecute = nameof(CanRunSelectedDeviceBatchAction), AllowConcurrentExecutions = true)]
    private Task RandomSelectedDevicesAsync(CancellationToken commandCancellationToken)
    {
        return StartTrackedBatchWorkflow(
            workflowCancellation => RunRandomSelectedDevicesAsync(workflowCancellation),
            commandCancellationToken);
    }

    private async Task RunRandomSelectedDevicesAsync(CancellationToken batchToken)
    {
        var targets = new List<BatchActionTarget>();
        Guid sessionId = Guid.NewGuid();
        try
        {
            DeviceRowViewModel[] selectedDevices = _allDeviceRows
                .Where(device => device.IsSelected)
                .ToArray();
            if (selectedDevices.Length == 0)
                return;

            MultipleDeviceConfiguration actionConfiguration =
                await LoadActionConfigurationSnapshotAsync(batchToken).ConfigureAwait(true);
            List<StoredDeviceConfig> storedConfigurationSnapshot = CreateStoredDevicesSnapshot();
            RandomDeviceRequest request = CreateRandomDeviceRequest(actionConfiguration);
            foreach (DeviceRowViewModel device in selectedDevices)
            {
                if (IsDeviceBusy(device))
                {
                    ShowBusyActionLog(device);
                    continue;
                }

                if (device.ConnectionStatus != AdbDeviceStatus.Online)
                {
                    SetDeviceLog(device, "Log_DeviceMustBeOnline");
                    continue;
                }

                IDeviceActionOperation? operation = _deviceActionCoordinatorService.TryStart(
                    device.Serial,
                    DeviceActionKind.BatchRandomDevice,
                    canCancel: true,
                    externalCancellationToken: batchToken,
                    sessionId: sessionId);
                if (operation != null)
                {
                    var target = new BatchActionTarget(
                        device,
                        operation,
                        deviceProfile: null,
                        simProfile: null,
                        randomDeviceRequest: CreateRandomDeviceRequestCopy(request),
                        deviceConfigurationSnapshot: CreateStoredDevicesSnapshot(storedConfigurationSnapshot));
                    RegisterBatchTarget(target);
                    targets.Add(target);
                }
                else
                {
                    ShowBusyActionLog(device);
                }
            }

            if (targets.Count == 0)
                return;


            await Task.WhenAll(targets.Select(target =>
                    StartBatchTargetWorker(
                        target,
                        () => RandomSelectedDeviceAsync(target, batchToken))))
                .ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            await SetBatchCancellationResultsAsync(targets, "Log_RandomDeviceCanceled")
                .ConfigureAwait(true);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Failed to randomize selected devices in Multiple Device screen.");
        }
        finally
        {
            CompleteBatchOwnedTargets(targets);
        }
    }

    [RelayCommand(CanExecute = nameof(CanRunSelectedDeviceBatchAction), AllowConcurrentExecutions = true)]
    private Task ChangeSelectedDevicesAsync(CancellationToken commandCancellationToken)
    {
        return StartTrackedBatchWorkflow(
            workflowCancellation => RunSelectedDeviceBatchActionAsync(
                MultipleDeviceBatchAction.ChangeAndWipe,
                workflowCancellation),
            commandCancellationToken);
    }

    [RelayCommand(CanExecute = nameof(CanRunSelectedDeviceBatchAction), AllowConcurrentExecutions = true)]
    private Task RandomChangeAndWipeSelectedDevicesAsync(CancellationToken commandCancellationToken)
    {
        return StartTrackedBatchWorkflow(
            RunRandomChangeAndWipeSelectedDevicesAsync,
            commandCancellationToken);
    }

    [RelayCommand(CanExecute = nameof(CanRunSelectedDeviceBatchAction), AllowConcurrentExecutions = true)]
    private Task ChangeSelectedDevicesWithoutWipeAsync(CancellationToken commandCancellationToken)
    {
        return StartTrackedBatchWorkflow(
            workflowCancellation => RunSelectedDeviceBatchActionAsync(
                MultipleDeviceBatchAction.ChangeWithoutWipe,
                workflowCancellation),
            commandCancellationToken);
    }

    [RelayCommand(CanExecute = nameof(CanRunSelectedDeviceBatchAction), AllowConcurrentExecutions = true)]
    private Task WipeSelectedDevicesWithoutChangeAsync(CancellationToken commandCancellationToken)
    {
        return StartTrackedBatchWorkflow(
            workflowCancellation => RunSelectedDeviceBatchActionAsync(
                MultipleDeviceBatchAction.WipeWithoutChange,
                workflowCancellation),
            commandCancellationToken);
    }

    [RelayCommand(CanExecute = nameof(CanRunSelectedDeviceBatchAction), AllowConcurrentExecutions = true)]
    private Task RandomSelectedSimsAsync(CancellationToken commandCancellationToken)
    {
        return StartTrackedBatchWorkflow(
            workflowCancellation => RunSelectedDeviceBatchActionAsync(null, workflowCancellation),
            commandCancellationToken);
    }

    [RelayCommand(CanExecute = nameof(CanRunSelectedDeviceBatchAction), AllowConcurrentExecutions = true)]
    private Task ChangeSelectedSimsAsync(CancellationToken commandCancellationToken)
    {
        return StartTrackedBatchWorkflow(
            workflowCancellation => RunSelectedDeviceBatchActionAsync(
                MultipleDeviceBatchAction.ChangeSim,
                workflowCancellation),
            commandCancellationToken);
    }

    [RelayCommand(CanExecute = nameof(CanRunSelectedDeviceBatchAction), AllowConcurrentExecutions = true)]
    private Task ChangeSelectedLocationsAsync(CancellationToken cancellationToken)
    {
        return StartLocationTimezoneWorkflowAsync(isLocation: true, cancellationToken);
    }

    [RelayCommand(CanExecute = nameof(CanRunSelectedDeviceBatchAction), AllowConcurrentExecutions = true)]
    private Task ChangeSelectedTimezonesAsync(CancellationToken cancellationToken)
    {
        return StartLocationTimezoneWorkflowAsync(isLocation: false, cancellationToken);
    }

    [RelayCommand(CanExecute = nameof(CanRunSelectedDeviceBatchAction), AllowConcurrentExecutions = true)]
    private Task InstallSelectedPackagesAsync(CancellationToken cancellationToken)
    {
        DeviceRowViewModel[] selectedDevices = _allDeviceRows
            .Where(device => device.IsSelected)
            .ToArray();
        if (selectedDevices.Length == 0)
            return Task.CompletedTask;

        return StartTrackedBatchWorkflow(
            workflowCancellation => RunSelectedInstallPackageWorkflowAsync(
                selectedDevices,
                workflowCancellation),
            cancellationToken);
    }

    private Task StartLocationTimezoneWorkflowAsync(
        bool isLocation,
        CancellationToken commandCancellationToken)
    {
        return StartTrackedBatchWorkflow(
            cancellationToken => RunSelectedLocationOrTimezoneAsync(isLocation, cancellationToken),
            commandCancellationToken);
    }

    private async Task RunSelectedLocationOrTimezoneAsync(
        bool isLocation,
        CancellationToken cancellationToken)
    {
        var targets = new List<BatchActionTarget>();
        Guid sessionId = Guid.NewGuid();
        try
        {
            DeviceRowViewModel[] selectedDevices = _allDeviceRows
                .Where(device => device.IsSelected)
                .ToArray();
            if (selectedDevices.Length == 0)
                return;

            _ = await LoadActionConfigurationSnapshotAsync(cancellationToken).ConfigureAwait(true);
            List<StoredDeviceConfig> storedConfigurationSnapshot = CreateStoredDevicesSnapshot();

            DeviceActionKind actionKind = isLocation
                ? DeviceActionKind.BatchChangeLocation
                : DeviceActionKind.BatchChangeTimezone;
            targets = await CreateReservedEligibleTargetsAsync(
                    selectedDevices,
                    cancellationToken,
                    actionKind,
                    sessionId,
                    storedConfigurationSnapshot)
                .ConfigureAwait(true);
            if (targets.Count == 0)
                return;

            ChangeLocationDialogResult? locationResult = null;
            ChangeTimezoneDialogResult? timezoneResult = null;
            if (isLocation)
            {
                locationResult = await _changeLocationDialogService
                    .ShowChangeLocationBatchAsync(targets.Count, cancellationToken)
                    .ConfigureAwait(true);
            }
            else
            {
                timezoneResult = await _changeTimezoneDialogService
                    .ShowChangeTimezoneBatchAsync(targets.Count, cancellationToken)
                    .ConfigureAwait(true);
            }

            if (locationResult == null && timezoneResult == null)
            {
                string canceledKey = isLocation
                    ? "Log_ChangeLocationCanceled"
                    : "Log_ChangeTimezoneCanceled";
                await SetBatchDialogDismissalResultsAsync(targets, canceledKey)
                    .ConfigureAwait(true);
                return;
            }

            locationResult = CloneLocationDialogResult(locationResult);
            timezoneResult = CloneTimezoneDialogResult(timezoneResult);

            Task[] operations = targets
                .Select(target => StartBatchTargetWorker(
                    target,
                    () => isLocation
                        ? ExecuteLocationBatchTargetAsync(target, locationResult!, cancellationToken)
                        : ExecuteTimezoneBatchTargetAsync(target, timezoneResult!, cancellationToken)))
                .ToArray();
            await Task.WhenAll(operations).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            await SetBatchCancellationResultsAsync(
                    targets,
                    isLocation
                        ? "Log_ChangeLocationCanceled"
                        : "Log_ChangeTimezoneCanceled")
                .ConfigureAwait(true);
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Failed to execute Multiple Device {Action} action.",
                isLocation ? "Location" : "Timezone");
        }
        finally
        {
            CompleteBatchOwnedTargets(targets);
        }
    }

    private async Task RunSelectedInstallPackageWorkflowAsync(
        IReadOnlyList<DeviceRowViewModel> selectedDevices,
        CancellationToken cancellationToken)
    {
        var targets = new List<BatchActionTarget>();
        Guid sessionId = Guid.NewGuid();
        try
        {
            _ = await LoadActionConfigurationSnapshotAsync(cancellationToken).ConfigureAwait(true);
            List<StoredDeviceConfig> storedConfigurationSnapshot = CreateStoredDevicesSnapshot();
            targets = await CreateReservedEligibleTargetsAsync(
                    selectedDevices,
                    cancellationToken,
                    DeviceActionKind.BatchInstallPackages,
                    sessionId,
                    storedConfigurationSnapshot)
                .ConfigureAwait(true);
            if (targets.Count == 0)
                return;

            InstallPackageBatchRequest? request = await _installPackageDialogService
                .ShowInstallPackageBatchAsync(targets.Count, cancellationToken)
                .ConfigureAwait(true);
            if (request == null)
            {
                await SetBatchDialogDismissalResultsAsync(
                        targets,
                        "Log_InstallPackageCanceled")
                    .ConfigureAwait(true);

                return;
            }

            request = CloneInstallPackageBatchRequest(request);

            Task[] operations = targets
                .Select(target => StartBatchTargetWorker(
                    target,
                    () => ExecuteInstallPackageTargetAsync(
                        target,
                        request,
                        cancellationToken)))
                .ToArray();
            await Task.WhenAll(operations).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            await SetBatchCancellationResultsAsync(targets, "Log_InstallPackageCanceled")
                .ConfigureAwait(true);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Failed to execute Multiple Device Install Package action.");
        }
        finally
        {
            CompleteBatchOwnedTargets(targets);
        }
    }

    private async Task ExecuteInstallPackageTargetAsync(
        BatchActionTarget target,
        InstallPackageBatchRequest request,
        CancellationToken cancellationToken)
    {
        using var targetCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            target.OperationToken,
            target.InvalidationToken);
        try
        {
            await _batchActionThrottle.WaitAsync(targetCancellation.Token).ConfigureAwait(false);
            try
            {
                targetCancellation.Token.ThrowIfCancellationRequested();
                if (!target.TryStartExecution())
                    return;

                if (!await IsExecutionTargetOnlineAsync(target, targetCancellation.Token)
                        .ConfigureAwait(false))
                {
                    return;
                }

                if (!IsCurrentTarget(target))
                    return;

                int successCount = 0;
                int totalCount = request.FilePaths.Count;
                InstallPackageResult? singlePackageResult = null;
                foreach (string filePath in request.FilePaths)
                {
                    targetCancellation.Token.ThrowIfCancellationRequested();
                    await RunOnUiContextAsync(() => SetTargetLog(
                            target,
                            "Log_InstallPackageInstalling"))
                        .ConfigureAwait(false);

                    InstallPackageResult result;
                    try
                    {
                        result = await _packageInstallService
                            .InstallAsync(
                                target.Serial,
                                filePath,
                                request.Options,
                                targetCancellation.Token)
                            .ConfigureAwait(false);
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
                            target.Serial,
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
                    await RunOnUiContextAsync(() => SetTargetLog(
                            target,
                            singleResult.MessageResourceKey,
                            singleResult.MessageArguments.ToArray()))
                        .ConfigureAwait(false);
                }
                else
                {
                    string summaryKey = successCount == totalCount
                        ? "Log_InstallPackageCompleteFormat"
                        : successCount > 0
                            ? "Log_InstallPackagePartialFormat"
                            : "Log_InstallPackageFailedFormat";
                    await RunOnUiContextAsync(() => SetTargetLog(
                            target,
                            summaryKey,
                            successCount,
                            totalCount))
                        .ConfigureAwait(false);
                }
            }
            finally
            {
                _batchActionThrottle.Release();
            }
        }
        catch (OperationCanceledException) when (target.IsInvalidated)
        {
        }
        catch (OperationCanceledException)
        {
            await SetTargetCancellationResultAsync(target, "Log_InstallPackageCanceled")
                .ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Failed to install packages for device {Serial}.",
                target.Serial);
            await RunOnUiContextAsync(() => SetTargetLog(
                    target,
                    "Log_InstallPackageAdbFailure"))
                .ConfigureAwait(false);
        }
        finally
        {
            CompleteBatchTarget(target);
        }
    }

    private Task StartTrackedBatchWorkflow(
        Func<CancellationToken, Task> workflow,
        CancellationToken commandCancellationToken)
    {
        var workflowCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            commandCancellationToken,
            _actionLifetimeCancellation.Token);
        var completion = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_batchWorkflowsLock)
            _batchWorkflows[completion.Task] = workflowCancellation;

        _ = CompleteTrackedBatchWorkflowAsync(
            workflow,
            workflowCancellation,
            completion);
        return completion.Task;
    }

    private async Task CompleteTrackedBatchWorkflowAsync(
        Func<CancellationToken, Task> workflow,
        CancellationTokenSource workflowCancellation,
        TaskCompletionSource completion)
    {
        try
        {
            await workflow(workflowCancellation.Token)
                .ConfigureAwait(false);
            completion.TrySetResult();
        }
        catch (OperationCanceledException) when (workflowCancellation.IsCancellationRequested)
        {
            completion.TrySetCanceled(workflowCancellation.Token);
        }
        catch (Exception exception)
        {
            completion.TrySetException(exception);
        }
        finally
        {
            lock (_batchWorkflowsLock)
                _batchWorkflows.Remove(completion.Task);

            workflowCancellation.Dispose();
        }
    }

    private async Task CancelBatchWorkflowsAsync()
    {
        Task[] workflows = RequestBatchWorkflowCancellation();

        if (workflows.Length == 0)
            return;

        try
        {
            await Task.WhenAll(workflows).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private Task[] RequestBatchWorkflowCancellation()
    {
        CancellationTokenSource[] cancellations;
        Task[] workflows;
        lock (_batchWorkflowsLock)
        {
            cancellations = _batchWorkflows.Values.ToArray();
            workflows = _batchWorkflows.Keys.ToArray();
        }

        foreach (CancellationTokenSource cancellation in cancellations)
        {
            try
            {
                cancellation.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }
        }

        return workflows;
    }

    private async Task<List<BatchActionTarget>> CreateReservedEligibleTargetsAsync(
        IReadOnlyList<DeviceRowViewModel> selectedDevices,
        CancellationToken cancellationToken,
        DeviceActionKind actionKind,
        Guid sessionId,
        IReadOnlyList<StoredDeviceConfig> storedConfigurationSnapshot)
    {
        async Task<(DeviceRowViewModel Device, bool IsOnline, bool IsBusy)> CheckAsync(
            DeviceRowViewModel device)
        {
            if (IsDeviceBusy(device))
                return (device, IsOnline: false, IsBusy: true);

            await _batchActionThrottle.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (IsDeviceBusy(device))
                    return (device, IsOnline: false, IsBusy: true);

                bool isOnline;
                try
                {
                    isOnline = await _deviceListService
                        .IsDeviceOnlineAsync(device.Serial, cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    _logger.LogWarning(
                        exception,
                        "Live initial online preflight failed for device {Serial}.",
                        device.Serial);
                    isOnline = false;
                }

                return (device, isOnline, IsBusy: false);
            }
            finally
            {
                _batchActionThrottle.Release();
            }
        }

        (DeviceRowViewModel Device, bool IsOnline, bool IsBusy)[] checks = await Task
            .WhenAll(selectedDevices.Select(CheckAsync))
            .ConfigureAwait(true);
        var targets = new List<BatchActionTarget>(checks.Length);
        foreach ((DeviceRowViewModel device, bool isOnline, bool isBusy) in checks)
        {
            if (isBusy)
            {
                await RunOnUiContextAsync(() => ShowBusyActionLog(device))
                    .ConfigureAwait(false);
                continue;
            }

            if (!isOnline)
            {
                await RunOnUiContextAsync(() => SetDeviceLog(device, "Log_DeviceMustBeOnline"))
                    .ConfigureAwait(true);
                continue;
            }

            if (device.ConnectionStatus != AdbDeviceStatus.Online)
            {
                await RunOnUiContextAsync(() =>
                    {
                        device.ConnectionStatus = AdbDeviceStatus.Online;
                        device.Status = GetConnectionStatusText(AdbDeviceStatus.Online);
                    })
                    .ConfigureAwait(true);
            }

            IDeviceActionOperation? operation = _deviceActionCoordinatorService.TryStart(
                device.Serial,
                actionKind,
                canCancel: true,
                externalCancellationToken: cancellationToken,
                sessionId: sessionId);
            if (operation == null)
            {
                await RunOnUiContextAsync(() => ShowBusyActionLog(device))
                    .ConfigureAwait(false);
                continue;
            }

            var target = new BatchActionTarget(
                device,
                operation,
                deviceProfile: null,
                simProfile: null,
                deviceConfigurationSnapshot: CreateStoredDevicesSnapshot(storedConfigurationSnapshot));
            RegisterBatchTarget(target);
            targets.Add(target);
        }

        return targets;
    }

    private async Task ExecuteLocationBatchTargetAsync(
        BatchActionTarget target,
        ChangeLocationDialogResult result,
        CancellationToken cancellationToken)
    {
        using var targetCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            target.OperationToken,
            target.InvalidationToken);
        try
        {
            await _batchActionThrottle.WaitAsync(targetCancellation.Token).ConfigureAwait(false);
            try
            {
                targetCancellation.Token.ThrowIfCancellationRequested();
                if (!target.TryStartExecution())
                    return;

                if (!await IsExecutionTargetOnlineAsync(target, targetCancellation.Token)
                        .ConfigureAwait(false))
                    return;
                if (!IsCurrentTarget(target))
                    return;

                await RunOnUiContextAsync(() => SetTargetLog(
                        target,
                        result.Mode == ChangeLocationMode.DeviceIp
                            ? "Log_ResolvingByIp"
                            : "Log_ApplyingLocation"))
                    .ConfigureAwait(false);
                DeviceLocationResult applied = result.Mode == ChangeLocationMode.DeviceIp
                    ? await _deviceLocationService
                        .ApplyAsync(target.Serial, result, targetCancellation.Token)
                        .ConfigureAwait(false)
                    : result.SelectedLocation == null
                        ? throw new InvalidOperationException("Batch Location selection is missing.")
                        : await _deviceLocationService
                            .ApplyCatalogLocationAsync(
                                target.Serial,
                                result.SelectedLocation,
                                targetCancellation.Token)
                            .ConfigureAwait(false);

                bool saved = await PersistLocationConfigAsync(
                        target.Serial,
                        result.Mode,
                        applied,
                        targetCancellation.Token,
                        target.DeviceConfigurationSnapshot)
                    .ConfigureAwait(false);
                if (!saved)
                    throw new InvalidOperationException("The device Location configuration could not be saved.");
                await RunOnUiContextAsync(() => SetTargetLog(target, "Log_ChangeLocationSuccess"))
                    .ConfigureAwait(false);
            }
            finally
            {
                _batchActionThrottle.Release();
            }
        }
        catch (OperationCanceledException) when (target.IsInvalidated)
        {
        }
        catch (OperationCanceledException)
        {
            await SetTargetCancellationResultAsync(target, "Log_ChangeLocationCanceled")
                .ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Failed to change Location for device {Serial}.",
                target.Serial);
            await RunOnUiContextAsync(() => SetTargetLog(target, "Log_ChangeLocationFailed"))
                .ConfigureAwait(false);
        }
        finally
        {
            CompleteBatchTarget(target);
        }
    }

    private async Task ExecuteTimezoneBatchTargetAsync(
        BatchActionTarget target,
        ChangeTimezoneDialogResult result,
        CancellationToken cancellationToken)
    {
        using var targetCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            target.OperationToken,
            target.InvalidationToken);
        try
        {
            await _batchActionThrottle.WaitAsync(targetCancellation.Token).ConfigureAwait(false);
            try
            {
                targetCancellation.Token.ThrowIfCancellationRequested();
                if (!target.TryStartExecution())
                    return;

                if (!await IsExecutionTargetOnlineAsync(target, targetCancellation.Token)
                        .ConfigureAwait(false))
                    return;
                if (!IsCurrentTarget(target))
                    return;

                await RunOnUiContextAsync(() => SetTargetLog(
                        target,
                        result.Mode == ChangeTimezoneMode.DeviceIp
                            ? "Log_ResolvingByIp"
                            : "Log_ApplyingTimezone"))
                    .ConfigureAwait(false);
                string appliedTimezone = await _deviceTimezoneService
                    .ApplyAsync(target.Serial, result, targetCancellation.Token)
                    .ConfigureAwait(false);
                bool saved = await PersistTimezoneConfigAsync(
                        target.Serial,
                        result.Mode,
                        appliedTimezone,
                        targetCancellation.Token,
                        target.DeviceConfigurationSnapshot)
                    .ConfigureAwait(false);
                if (!saved)
                    throw new InvalidOperationException("The device Timezone configuration could not be saved.");
                await RunOnUiContextAsync(() => SetTargetLog(target, "Log_ChangeTimezoneSuccess"))
                    .ConfigureAwait(false);
            }
            finally
            {
                _batchActionThrottle.Release();
            }
        }
        catch (OperationCanceledException) when (target.IsInvalidated)
        {
        }
        catch (OperationCanceledException)
        {
            await SetTargetCancellationResultAsync(target, "Log_ChangeTimezoneCanceled")
                .ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Failed to change Timezone for device {Serial}.",
                target.Serial);
            await RunOnUiContextAsync(() => SetTargetLog(target, "Log_ChangeTimezoneFailed"))
                .ConfigureAwait(false);
        }
        finally
        {
            CompleteBatchTarget(target);
        }
    }

    private async Task<bool> IsExecutionTargetOnlineAsync(
        BatchActionTarget target,
        CancellationToken cancellationToken)
    {
        bool isOnline;
        try
        {
            isOnline = await _deviceListService
                .IsDeviceOnlineAsync(target.Serial, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                exception,
                "Live execution online check failed for device {Serial}.",
                target.Serial);
            isOnline = false;
        }

        if (isOnline)
            return true;

        await RunOnUiContextAsync(() => SetTargetLog(target, "Log_DeviceMustBeOnline"))
            .ConfigureAwait(false);
        return false;
    }

    private async Task<bool> PersistLocationConfigAsync(
        string serial,
        ChangeLocationMode mode,
        DeviceLocationResult result,
        CancellationToken cancellationToken,
        IList<StoredDeviceConfig>? configurationSnapshot = null)
    {
        await _deviceRefreshLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            bool updated = await _deviceConfigService.SaveLocationConfigAsync(
                    configurationSnapshot ?? _storedDevices,
                    serial,
                    mode,
                    result.Latitude,
                    result.Longitude,
                    result.CountryCode,
                    result.CityName,
                    cancellationToken)
                .ConfigureAwait(false);
            if (updated && configurationSnapshot != null)
                MergeStoredDeviceSnapshot(configurationSnapshot, serial);
            return updated;
        }
        finally
        {
            _deviceRefreshLock.Release();
        }
    }

    private async Task<bool> PersistTimezoneConfigAsync(
        string serial,
        ChangeTimezoneMode mode,
        string timezone,
        CancellationToken cancellationToken,
        IList<StoredDeviceConfig>? configurationSnapshot = null)
    {
        await _deviceRefreshLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            bool updated = await _deviceConfigService.SaveTimezoneConfigAsync(
                    configurationSnapshot ?? _storedDevices,
                    serial,
                    mode,
                    timezone,
                    cancellationToken)
                .ConfigureAwait(false);
            if (updated && configurationSnapshot != null)
                MergeStoredDeviceSnapshot(configurationSnapshot, serial);
            return updated;
        }
        finally
        {
            _deviceRefreshLock.Release();
        }
    }

    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task ViewDeviceInfoAsync(DeviceRowViewModel? device)
    {
        await GetContextOnlineDeviceAsync(device).ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task CopySerialAsync(DeviceRowViewModel? device, CancellationToken cancellationToken)
    {
        if (device == null || string.IsNullOrWhiteSpace(device.Serial))
            return;

        try
        {
            await RunOnUiContextAsync(() => System.Windows.Clipboard.SetText(device.Serial))
                .ConfigureAwait(true);
            SetDeviceLog(device, "Log_CopySerialSuccess");
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Failed to copy serial for device {Serial}.", device.Serial);
            SetDeviceLog(device, "Log_CopySerialFailed");
        }
    }

    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task RefreshContextMenuStateAsync(DeviceRowViewModel? device)
    {
        device = await GetContextOnlineDeviceAsync(device).ConfigureAwait(true);
        if (device == null)
            return;

        device.IsContextMenuStateLoading = true;
        try
        {
            Task<GooglePackageState> googleState = _deviceActionService
                .GetGooglePackageStateAsync(device.Serial, CancellationToken.None);
            Task<bool> wifiState = _deviceActionService
                .GetWifiEnabledAsync(device.Serial, CancellationToken.None);
            try
            {
                await Task.WhenAll(googleState, wifiState).ConfigureAwait(true);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                _logger.LogError(exception, "Failed to refresh context menu state for {Serial}.", device.Serial);
            }

            if (googleState.IsCompletedSuccessfully)
            {
                device.IsGmsDisabled = googleState.Result.IsGmsDisabled;
                device.IsPlayStoreDisabled = googleState.Result.IsPlayStoreDisabled;
            }

            if (wifiState.IsCompletedSuccessfully)
                device.IsWifiEnabled = wifiState.Result;
        }
        finally
        {
            device.IsContextMenuStateLoading = false;
        }
    }

    [RelayCommand(AllowConcurrentExecutions = true)]
    private Task ToggleGmsAsync(DeviceRowViewModel? device)
    {
        return ToggleGooglePackageAsync(device, isGms: true);
    }

    [RelayCommand(AllowConcurrentExecutions = true)]
    private Task TogglePlayStoreAsync(DeviceRowViewModel? device)
    {
        return ToggleGooglePackageAsync(device, isGms: false);
    }

    private async Task ToggleGooglePackageAsync(DeviceRowViewModel? device, bool isGms)
    {
        device = await GetContextOnlineDeviceAsync(device).ConfigureAwait(true);
        if (device == null)
            return;

        try
        {
            GooglePackageState state = await _deviceActionService
                .GetGooglePackageStateAsync(device.Serial, CancellationToken.None)
                .ConfigureAwait(true);
            bool enabled = isGms ? state.IsGmsDisabled : state.IsPlayStoreDisabled;
            if (isGms)
            {
                await _deviceActionService
                    .SetGmsEnabledAsync(device.Serial, enabled, CancellationToken.None)
                    .ConfigureAwait(true);
                device.IsGmsDisabled = !enabled;
            }
            else
            {
                await _deviceActionService
                    .SetPlayStoreEnabledAsync(device.Serial, enabled, CancellationToken.None)
                    .ConfigureAwait(true);
                device.IsPlayStoreDisabled = !enabled;
            }

            SetDeviceLog(device, (isGms, enabled) switch
            {
                (true, true) => "Log_GmsEnabled",
                (true, false) => "Log_GmsDisabled",
                (false, true) => "Log_PlayStoreEnabled",
                _ => "Log_PlayStoreDisabled"
            });
        }
        catch (OperationCanceledException)
        {
            SetDeviceLog(device, "Log_Ready");
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Failed to toggle package state for {Serial}.", device.Serial);
            SetDeviceLog(
                device,
                isGms ? "Log_GmsToggleFailed" : "Log_PlayStoreToggleFailed");
        }
    }

    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task ToggleWifiAsync(DeviceRowViewModel? device)
    {
        device = await GetContextOnlineDeviceAsync(device).ConfigureAwait(true);
        if (device == null)
            return;

        try
        {
            bool isEnabled = await _deviceActionService
                .GetWifiEnabledAsync(device.Serial, CancellationToken.None)
                .ConfigureAwait(true);
            bool enabled = !isEnabled;
            await _deviceActionService
                .SetWifiEnabledAsync(device.Serial, enabled, CancellationToken.None)
                .ConfigureAwait(true);
            device.IsWifiEnabled = enabled;
            SetDeviceLog(device, enabled ? "Log_WifiEnabled" : "Log_WifiDisabled");
        }
        catch (OperationCanceledException)
        {
            SetDeviceLog(device, "Log_Ready");
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Failed to toggle Wi-Fi for {Serial}.", device.Serial);
            SetDeviceLog(device, "Log_WifiToggleFailed");
        }
    }

    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task RebootDeviceAsync(DeviceRowViewModel? device)
    {
        device = await GetContextOnlineDeviceAsync(device).ConfigureAwait(true);
        if (device == null)
            return;

        try
        {
            SetDeviceLog(device, "Log_RebootingDevice");
            await _deviceActionService
                .RebootAsync(device.Serial, CancellationToken.None)
                .ConfigureAwait(true);
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

    [RelayCommand(CanExecute = nameof(CanExecuteContextDeviceAction), AllowConcurrentExecutions = true)]
    private async Task DeleteDeviceAsync(DeviceRowViewModel? device)
    {
        if (device == null)
            return;

        using IDeviceActionOperation? operation = TryStartContextAction(device);
        if (operation == null)
            return;

        string serial = device.Serial;
        string name = device.Name;
        CancellationToken cancellationToken = operation.CancellationToken;
        try
        {
            bool confirmed = await _deviceActionConfirmationDialogService
                .ConfirmDeleteDeviceAsync(name, serial, cancellationToken)
                .ConfigureAwait(true);
            if (!confirmed)
            {
                SetContextDialogDismissalLog(device, operation, "Log_DeleteDeviceCanceled");
                return;
            }

            SetDeviceLog(device, "Log_DeletingDevice");
            await _deviceRefreshLock.WaitAsync(cancellationToken).ConfigureAwait(true);
            try
            {
                DeviceDeleteResult result = await _deviceListService
                    .DeleteSavedDeviceAsync(serial, cancellationToken)
                    .ConfigureAwait(true);
                if (!result.Removed)
                {
                    SetDeviceLog(device, "Log_DeleteDeviceFailed");
                    return;
                }

                _randomDeviceProfiles.Remove(serial);
                _randomSimProfiles.Remove(serial);
                ApplyDeviceListSnapshot(result.Snapshot);
            }
            finally
            {
                _deviceRefreshLock.Release();
            }
        }
        catch (OperationCanceledException)
        {
            SetContextOperationCancellationLog(device, operation, "Log_DeleteDeviceCanceled");
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Failed to delete device {Serial}.", device.Serial);
            SetDeviceLog(device, "Log_DeleteDeviceFailed");
        }
    }

    private async Task<DeviceRowViewModel?> GetContextOnlineDeviceAsync(DeviceRowViewModel? device)
    {
        if (device == null)
            return null;

        if (IsDeviceBusy(device))
        {
            ShowBusyActionLog(device);
            return null;
        }

        if (device.ConnectionStatus != AdbDeviceStatus.Online)
        {
            SetDeviceLog(device, "Log_DeviceMustBeOnline");
            return null;
        }

        bool isOnline;
        try
        {
            isOnline = await _deviceListService
                .IsDeviceOnlineAsync(device.Serial, CancellationToken.None)
                .ConfigureAwait(true);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogWarning(exception, "Live context-menu online check failed for {Serial}.", device.Serial);
            isOnline = false;
        }

        if (isOnline)
            return device;

        SetDeviceLog(device, "Log_DeviceMustBeOnline");
        return null;
    }

    private IDeviceActionOperation? TryStartContextAction(DeviceRowViewModel device)
    {
        IDeviceActionOperation? operation = _deviceActionCoordinatorService.TryStart(
            device.Serial,
            DeviceActionKind.DeleteDevice,
            canCancel: false,
            externalCancellationToken: _actionLifetimeCancellation.Token);
        TrackContextOperation(operation);
        return operation;
    }

    private void TrackContextOperation(IDeviceActionOperation? operation)
    {
        if (operation == null)
            return;

        lock (_activeContextOperationsLock)
        {
            if (_activeContextOperationIds.Count == 0)
            {
                _activeContextOperationsCompletion = new TaskCompletionSource(
                    TaskCreationOptions.RunContinuationsAsynchronously);
            }

            _activeContextOperationIds.Add(operation.OperationId);
        }
    }

    private async Task RunSelectedDeviceBatchActionAsync(
        MultipleDeviceBatchAction? action,
        CancellationToken cancellationToken)
    {
        var targets = new List<BatchActionTarget>();
        Guid sessionId = Guid.NewGuid();
        try
        {
            DeviceRowViewModel[] selectedDevices = _allDeviceRows
                .Where(device => device.IsSelected)
                .ToArray();
            if (selectedDevices.Length == 0)
                return;

            MultipleDeviceConfiguration actionConfiguration =
                await LoadActionConfigurationSnapshotAsync(cancellationToken).ConfigureAwait(true);
            List<StoredDeviceConfig> storedConfigurationSnapshot = CreateStoredDevicesSnapshot();
            DeviceChangeOptions changeOptions = DeviceChangeOptionsHelper.CreateNormalizedCopy(
                actionConfiguration.ChangeOptions);
            bool changeSimEnabled = actionConfiguration.ChangeConfig.ChangeSimEnabled;
            CarrierCountryOption? country = CreateCountryOption(actionConfiguration.ChangeConfig);
            CarrierOption? carrier = CreateCarrierOption(actionConfiguration.ChangeConfig);

            DeviceActionKind actionKind = action switch
            {
                MultipleDeviceBatchAction.ChangeAndWipe => DeviceActionKind.BatchChangeDevice,
                MultipleDeviceBatchAction.ChangeWithoutWipe => DeviceActionKind.BatchChangeWithoutWipe,
                MultipleDeviceBatchAction.WipeWithoutChange => DeviceActionKind.BatchWipe,
                MultipleDeviceBatchAction.ChangeSim => DeviceActionKind.BatchChangeSim,
                null => DeviceActionKind.BatchRandomSim,
                _ => throw new ArgumentOutOfRangeException(nameof(action), action, null)
            };
            foreach (DeviceRowViewModel device in selectedDevices)
            {
                if (IsDeviceBusy(device))
                {
                    ShowBusyActionLog(device);
                    continue;
                }

                if (device.ConnectionStatus != AdbDeviceStatus.Online)
                {
                    SetDeviceLog(device, "Log_DeviceMustBeOnline");
                    continue;
                }

                DeviceInfoApiDevice? deviceProfile = null;
                if (action is MultipleDeviceBatchAction.ChangeAndWipe or MultipleDeviceBatchAction.ChangeWithoutWipe)
                {
                    if (!_randomDeviceProfiles.TryGetValue(device.Serial, out deviceProfile))
                    {
                        SetDeviceLog(device, "Log_RandomDeviceRequired");
                        continue;
                    }
                }
                else if (action == MultipleDeviceBatchAction.ChangeSim)
                {
                    _randomDeviceProfiles.TryGetValue(device.Serial, out deviceProfile);
                }

                SimProfile? simProfile = null;
                if (action == MultipleDeviceBatchAction.ChangeSim
                    && !_randomSimProfiles.TryGetValue(device.Serial, out simProfile))
                {
                    SetDeviceLog(device, "Log_RandomSimRequired");
                    continue;
                }

                IDeviceActionOperation? operation = _deviceActionCoordinatorService.TryStart(
                    device.Serial,
                    actionKind,
                    canCancel: true,
                    externalCancellationToken: cancellationToken,
                    sessionId: sessionId);
                if (operation == null)
                {
                    ShowBusyActionLog(device);
                    continue;
                }

                var target = new BatchActionTarget(
                    device,
                    operation,
                    CloneDeviceProfile(deviceProfile),
                    CloneSimProfile(simProfile),
                    DeviceChangeOptionsHelper.CreateNormalizedCopy(changeOptions),
                    changeSimEnabled,
                    CloneCountryOption(country),
                    CloneCarrierOption(carrier),
                    deviceConfigurationSnapshot: CreateStoredDevicesSnapshot(storedConfigurationSnapshot));
                RegisterBatchTarget(target);
                targets.Add(target);
            }

            if (targets.Count == 0)
                return;

            if (action != null)
            {
                bool confirmed = await _deviceActionConfirmationDialogService
                    .ConfirmMultipleAsync(action.Value, targets.Count, cancellationToken)
                    .ConfigureAwait(true);
                if (!confirmed)
                {
                    await SetBatchDialogDismissalResultsAsync(
                            targets,
                            GetCanceledLogKey(action.Value))
                        .ConfigureAwait(true);
                    return;
                }
            }

            Task[] operations = targets
                .Select(target => StartBatchTargetWorker(
                    target,
                    () => ExecuteBatchActionTargetAsync(
                        action,
                        target,
                        cancellationToken)))
                .ToArray();
            await Task.WhenAll(operations).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            await SetBatchCancellationResultsAsync(
                    targets,
                    GetBatchCancellationLogKey(action))
                .ConfigureAwait(true);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Failed to execute a Multiple Device batch action.");
        }
        finally
        {
            CompleteBatchOwnedTargets(targets);
        }
    }

    private async Task RunRandomChangeAndWipeSelectedDevicesAsync(CancellationToken cancellationToken)
    {
        var targets = new List<BatchActionTarget>();
        Guid sessionId = Guid.NewGuid();
        try
        {
            DeviceRowViewModel[] selectedDevices = _allDeviceRows
                .Where(device => device.IsSelected)
                .ToArray();
            if (selectedDevices.Length == 0)
                return;

            MultipleDeviceConfiguration actionConfiguration =
                await LoadActionConfigurationSnapshotAsync(cancellationToken).ConfigureAwait(true);
            List<StoredDeviceConfig> storedConfigurationSnapshot = CreateStoredDevicesSnapshot();
            DeviceChangeOptions changeOptions = DeviceChangeOptionsHelper.CreateNormalizedCopy(
                actionConfiguration.ChangeOptions);
            bool changeSimEnabled = actionConfiguration.ChangeConfig.ChangeSimEnabled;
            RandomDeviceRequest randomRequest = CreateRandomDeviceRequest(actionConfiguration);

            foreach (DeviceRowViewModel device in selectedDevices)
            {
                if (IsDeviceBusy(device))
                {
                    ShowBusyActionLog(device);
                    continue;
                }

                if (device.ConnectionStatus != AdbDeviceStatus.Online)
                {
                    SetDeviceLog(device, "Log_DeviceMustBeOnline");
                    continue;
                }

                IDeviceActionOperation? operation = _deviceActionCoordinatorService.TryStart(
                    device.Serial,
                    DeviceActionKind.BatchRandomChangeAndWipe,
                    canCancel: true,
                    externalCancellationToken: cancellationToken,
                    sessionId: sessionId);
                if (operation == null)
                {
                    ShowBusyActionLog(device);
                    continue;
                }

                var target = new BatchActionTarget(
                    device,
                    operation,
                    deviceProfile: null,
                    simProfile: null,
                    DeviceChangeOptionsHelper.CreateNormalizedCopy(changeOptions),
                    changeSimEnabled,
                    country: null,
                    carrier: null,
                    randomDeviceRequest: CreateRandomDeviceRequestCopy(randomRequest),
                    deviceConfigurationSnapshot: CreateStoredDevicesSnapshot(storedConfigurationSnapshot));
                RegisterBatchTarget(target);
                targets.Add(target);
            }

            if (targets.Count == 0)
                return;

            bool confirmed = await _deviceActionConfirmationDialogService
                .ConfirmMultipleAsync(MultipleDeviceBatchAction.ChangeAndWipe, targets.Count, cancellationToken)
                .ConfigureAwait(true);
            if (!confirmed)
            {
                await SetBatchDialogDismissalResultsAsync(
                        targets,
                        GetCanceledLogKey(MultipleDeviceBatchAction.ChangeAndWipe))
                    .ConfigureAwait(true);
                return;
            }

            await Task.WhenAll(targets.Select(target =>
                    StartBatchTargetWorker(
                        target,
                        () => ExecuteRandomChangeAndWipeTargetAsync(target, cancellationToken))))
                .ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            await SetBatchCancellationResultsAsync(targets, "Log_ChangeDeviceCanceled")
                .ConfigureAwait(true);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Failed to randomize, change, and wipe selected devices.");
        }
        finally
        {
            CompleteBatchOwnedTargets(targets);
        }
    }

    private async Task ExecuteBatchActionTargetAsync(
        MultipleDeviceBatchAction? action,
        BatchActionTarget target,
        CancellationToken cancellationToken)
    {
        using var targetCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            target.OperationToken,
            target.InvalidationToken);
        try
        {
            await _batchActionThrottle.WaitAsync(targetCancellation.Token).ConfigureAwait(false);
            try
            {
                targetCancellation.Token.ThrowIfCancellationRequested();
                if (!target.TryStartExecution())
                    return;

                if (!await CanStartBatchTargetAsync(target, targetCancellation.Token).ConfigureAwait(false))
                    return;

                await RunOnUiContextAsync(() => SetTargetLog(target, GetStartLogKey(action)))
                    .ConfigureAwait(false);
                IProgress<DeviceChangeStage> progress = CreateDeviceChangeProgress(target, action);
                switch (action)
                {
                    case MultipleDeviceBatchAction.ChangeAndWipe:
                        await _deviceChangeService.ChangeAsync(
                                target.Serial,
                                target.DeviceProfile!,
                                target.ChangeSimEnabled,
                                target.ChangeOptions!,
                                progress,
                                targetCancellation.Token)
                            .ConfigureAwait(false);
                        break;
                    case MultipleDeviceBatchAction.ChangeWithoutWipe:
                        await _deviceChangeService.ChangeWithoutWipeAsync(
                                target.Serial,
                                target.DeviceProfile!,
                                target.ChangeSimEnabled,
                                target.ChangeOptions!,
                                progress,
                                targetCancellation.Token)
                            .ConfigureAwait(false);
                        break;
                    case MultipleDeviceBatchAction.WipeWithoutChange:
                        await _deviceChangeService.WipeWithoutChangeAsync(
                                target.Serial,
                                target.ChangeOptions!,
                                progress,
                                targetCancellation.Token)
                            .ConfigureAwait(false);
                        break;
                    case MultipleDeviceBatchAction.ChangeSim:
                    {
                        SimProfile editedProfile = CreateEditedSimProfile(target.DeviceProfile, target.SimProfile!);
                        await _deviceChangeService.ChangeSimAsync(
                                target.Serial,
                                editedProfile,
                                targetCancellation.Token)
                            .ConfigureAwait(false);
                        await RunOnUiContextAsync(() => ApplyRandomSimInfo(target, editedProfile))
                            .ConfigureAwait(false);
                        break;
                    }
                    case null:
                    {
                        SimProfile randomSim = _simProfileService.CreateRandomProfile(
                            target.Country,
                            target.Carrier);
                        await RunOnUiContextAsync(() => ApplyRandomSimInfo(target, randomSim))
                            .ConfigureAwait(false);
                        break;
                    }
                    default:
                        throw new ArgumentOutOfRangeException(nameof(action), action, null);
                }

                await RunOnUiContextAsync(() => SetTargetLog(target, GetSuccessLogKey(action)))
                    .ConfigureAwait(false);
            }
            finally
            {
                _batchActionThrottle.Release();
            }
        }
        catch (OperationCanceledException) when (target.IsInvalidated)
        {
        }
        catch (OperationCanceledException)
        {
            await SetTargetCancellationResultAsync(target, GetBatchCancellationLogKey(action))
                .ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Failed to execute {Action} for device {Serial}.", action, target.Serial);
            await RunOnUiContextAsync(() => SetTargetLog(target, GetFailureLogKey(action)))
                .ConfigureAwait(false);
        }
        finally
        {
            CompleteBatchTarget(target);
        }
    }

    private async Task ExecuteRandomChangeAndWipeTargetAsync(
        BatchActionTarget target,
        CancellationToken cancellationToken)
    {
        using var targetCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            target.OperationToken,
            target.InvalidationToken);
        try
        {
            await _batchActionThrottle.WaitAsync(targetCancellation.Token).ConfigureAwait(false);
            try
            {
                targetCancellation.Token.ThrowIfCancellationRequested();
                if (!target.TryStartExecution()
                    || !await CanStartBatchTargetAsync(target, targetCancellation.Token).ConfigureAwait(false))
                {
                    return;
                }

                await RunOnUiContextAsync(() => SetTargetLog(target, "Log_RandomAndChangeDevice"))
                    .ConfigureAwait(false);
                RandomDeviceResult randomResult = await _randomDeviceService
                    .CreateRandomProfileAsync(target.RandomDeviceRequest!, targetCancellation.Token)
                    .ConfigureAwait(false);
                if (randomResult.Status == RandomDeviceStatus.LoginRequired)
                {
                    await RunOnUiContextAsync(() => SetTargetLog(target, "Log_RandomDeviceLoginRequired"))
                        .ConfigureAwait(false);
                    return;
                }

                if (randomResult.Status != RandomDeviceStatus.Created || randomResult.Profile == null)
                {
                    await RunOnUiContextAsync(() => SetTargetLog(target, "Log_RandomDeviceFailed"))
                        .ConfigureAwait(false);
                    return;
                }

                DeviceInfoApiDevice profile = CloneDeviceProfile(randomResult.Profile)!;
                await RunOnUiContextAsync(() => ApplyRandomDeviceInfo(target, profile.Clone()))
                    .ConfigureAwait(false);

                await _deviceChangeService.ChangeAsync(
                        target.Serial,
                        profile,
                        target.ChangeSimEnabled,
                        target.ChangeOptions!,
                        CreateDeviceChangeProgress(target, MultipleDeviceBatchAction.ChangeAndWipe),
                        targetCancellation.Token)
                    .ConfigureAwait(false);
                await RunOnUiContextAsync(() => SetTargetLog(target, "Log_ChangeDeviceSuccess"))
                    .ConfigureAwait(false);
            }
            finally
            {
                _batchActionThrottle.Release();
            }
        }
        catch (OperationCanceledException) when (target.IsInvalidated)
        {
        }
        catch (OperationCanceledException)
        {
            await SetTargetCancellationResultAsync(target, "Log_ChangeDeviceCanceled")
                .ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception,
                "Failed to randomize, change, and wipe device {Serial}.", target.Serial);
            await RunOnUiContextAsync(() => SetTargetLog(target, "Log_ChangeDeviceFailed"))
                .ConfigureAwait(false);
        }
        finally
        {
            CompleteBatchTarget(target);
        }
    }

    private DeviceChangeOptions CreateCurrentChangeOptions()
    {
        return DeviceChangeOptionsHelper.CreateNormalizedCopy(_changeOptions, UseDefaultChangeMode);
    }

    private async Task<MultipleDeviceConfiguration> LoadActionConfigurationSnapshotAsync(
        CancellationToken cancellationToken)
    {
        await FlushPendingConfigurationSaveAsync().ConfigureAwait(false);
        await RefreshStoredDevicesFromDiskAsync(cancellationToken).ConfigureAwait(false);
        MultipleDeviceConfiguration configuration = await _multipleDeviceConfigService
            .LoadAsync(cancellationToken)
            .ConfigureAwait(false);
        return new MultipleDeviceConfiguration
        {
            ChangeConfig = new MultipleDeviceChangeConfig
            {
                Brand = configuration.ChangeConfig.Brand,
                AndroidVersion = configuration.ChangeConfig.AndroidVersion,
                Model = configuration.ChangeConfig.Model,
                CountryIso = configuration.ChangeConfig.CountryIso,
                CountryName = configuration.ChangeConfig.CountryName,
                Carrier = configuration.ChangeConfig.Carrier,
                CarrierMcc = configuration.ChangeConfig.CarrierMcc,
                CarrierMnc = configuration.ChangeConfig.CarrierMnc,
                ChangeSimEnabled = configuration.ChangeConfig.ChangeSimEnabled,
                UseIntegritySecurityPatch = configuration.ChangeConfig.UseIntegritySecurityPatch
            },
            ChangeOptions = DeviceChangeOptionsHelper.CreateNormalizedCopy(configuration.ChangeOptions)
        };
    }

    private async Task RefreshStoredDevicesFromDiskAsync(CancellationToken cancellationToken)
    {
        await FlushPendingDeviceEditsAsync().ConfigureAwait(false);
        await _deviceRefreshLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            DeviceListSnapshot snapshot = await _deviceListService
                .LoadSnapshotAsync(cancellationToken)
                .ConfigureAwait(false);
            await RunOnUiContextAsync(() =>
            {
                _storedDevices = snapshot.StoredDevices
                    .Select(CloneStoredDeviceConfig)
                    .ToList();
            }).ConfigureAwait(false);
        }
        finally
        {
            _deviceRefreshLock.Release();
        }
    }

    private List<StoredDeviceConfig> CreateStoredDevicesSnapshot()
    {
        return CreateStoredDevicesSnapshot(_storedDevices);
    }

    private static List<StoredDeviceConfig> CreateStoredDevicesSnapshot(
        IReadOnlyList<StoredDeviceConfig> storedDevices)
    {
        return storedDevices
            .Select(CloneStoredDeviceConfig)
            .ToList();
    }

    private void MergeStoredDeviceSnapshot(
        IList<StoredDeviceConfig> snapshot,
        string serial)
    {
        StoredDeviceConfig? updated = snapshot
            .FirstOrDefault(device => SerialEquals(device.Serial, serial));
        if (updated == null)
            return;

        StoredDeviceConfig? current = _storedDevices
            .FirstOrDefault(device => SerialEquals(device.Serial, serial));
        if (current == null)
            return;

        int index = _storedDevices.IndexOf(current);
        _storedDevices[index] = CloneStoredDeviceConfig(updated);
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

    private IProgress<DeviceChangeStage> CreateDeviceChangeProgress(
        BatchActionTarget target,
        MultipleDeviceBatchAction? action)
    {
        return new Progress<DeviceChangeStage>(stage =>
            SetTargetLog(target, stage switch
            {
                DeviceChangeStage.Preparing => "Log_ChangeDevicePreparing",
                DeviceChangeStage.ApplyingProfile => "Log_ChangeDeviceApplyingProfile",
                DeviceChangeStage.ClearingData => "Log_ChangeDeviceClearingData",
                DeviceChangeStage.Rebooting => "Log_ChangeDeviceRebooting",
                DeviceChangeStage.WaitingForDevice => "Log_WaitingForDevice",
                DeviceChangeStage.Verifying => "Log_ChangeDeviceVerifying",
                DeviceChangeStage.Completed => GetSuccessLogKey(action),
                _ => GetStartLogKey(action)
            }));
    }

    private static string GetStartLogKey(MultipleDeviceBatchAction? action)
    {
        return action switch
        {
            MultipleDeviceBatchAction.ChangeAndWipe => "Log_ChangeDevice",
            MultipleDeviceBatchAction.ChangeWithoutWipe => "Log_ChangeWithoutWipe",
            MultipleDeviceBatchAction.WipeWithoutChange => "Log_WipeWithoutChange",
            MultipleDeviceBatchAction.ChangeSim => "Log_ChangeSim",
            null => "Log_RandomSim",
            _ => throw new ArgumentOutOfRangeException(nameof(action), action, null)
        };
    }

    private static string GetSuccessLogKey(MultipleDeviceBatchAction? action)
    {
        return action switch
        {
            MultipleDeviceBatchAction.ChangeAndWipe => "Log_ChangeDeviceSuccess",
            MultipleDeviceBatchAction.ChangeWithoutWipe => "Log_ChangeWithoutWipeSuccess",
            MultipleDeviceBatchAction.WipeWithoutChange => "Log_WipeWithoutChangeSuccess",
            MultipleDeviceBatchAction.ChangeSim => "Log_ChangeSimSuccess",
            null => "Log_RandomSimSuccess",
            _ => throw new ArgumentOutOfRangeException(nameof(action), action, null)
        };
    }

    private static string GetFailureLogKey(MultipleDeviceBatchAction? action)
    {
        return action switch
        {
            MultipleDeviceBatchAction.ChangeAndWipe => "Log_ChangeDeviceFailed",
            MultipleDeviceBatchAction.ChangeWithoutWipe => "Log_ChangeWithoutWipeFailed",
            MultipleDeviceBatchAction.WipeWithoutChange => "Log_WipeWithoutChangeFailed",
            MultipleDeviceBatchAction.ChangeSim => "Log_ChangeSimFailed",
            null => "Log_RandomSimFailed",
            _ => throw new ArgumentOutOfRangeException(nameof(action), action, null)
        };
    }

    private static string GetCanceledLogKey(MultipleDeviceBatchAction action)
    {
        return action switch
        {
            MultipleDeviceBatchAction.ChangeAndWipe => "Log_ChangeDeviceCanceled",
            MultipleDeviceBatchAction.ChangeWithoutWipe => "Log_ChangeWithoutWipeCanceled",
            MultipleDeviceBatchAction.WipeWithoutChange => "Log_WipeWithoutChangeCanceled",
            MultipleDeviceBatchAction.ChangeSim => "Log_ChangeSimCanceled",
            _ => throw new ArgumentOutOfRangeException(nameof(action), action, null)
        };
    }

    private static string GetBatchCancellationLogKey(MultipleDeviceBatchAction? action)
    {
        return action.HasValue
            ? GetCanceledLogKey(action.Value)
            : "Log_RandomSimCanceled";
    }

    private async Task RandomSelectedDeviceAsync(
        BatchActionTarget target,
        CancellationToken cancellationToken)
    {
        using var targetCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            target.OperationToken,
            target.InvalidationToken);
        try
        {
            await _batchActionThrottle.WaitAsync(targetCancellation.Token).ConfigureAwait(false);
            try
            {
                targetCancellation.Token.ThrowIfCancellationRequested();
                if (!target.TryStartExecution())
                    return;

                if (!await CanStartBatchTargetAsync(target, targetCancellation.Token).ConfigureAwait(false))
                {
                    return;
                }

                await RunOnUiContextAsync(() => SetTargetLog(target, "Log_RandomDevice"))
                    .ConfigureAwait(false);
                RandomDeviceResult result = await _randomDeviceService
                    .CreateRandomProfileAsync(target.RandomDeviceRequest!, targetCancellation.Token)
                    .ConfigureAwait(false);

                await RunOnUiContextAsync(() =>
                {
                    switch (result.Status)
                    {
                        case RandomDeviceStatus.Created when result.Profile != null:
                            ApplyRandomDeviceInfo(target, result.Profile.Clone());
                            SetTargetLog(target, "Log_RandomDeviceSuccess");
                            break;
                        case RandomDeviceStatus.LoginRequired:
                            SetTargetLog(target, "Log_RandomDeviceLoginRequired");
                            break;
                        default:
                            SetTargetLog(target, "Log_RandomDeviceFailed");
                            break;
                    }
                }).ConfigureAwait(false);
            }
            finally
            {
                _batchActionThrottle.Release();
            }
        }
        catch (OperationCanceledException) when (target.IsInvalidated)
        {
        }
        catch (OperationCanceledException)
        {
            await SetTargetCancellationResultAsync(target, "Log_RandomDeviceCanceled")
                .ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Failed to randomize device {Serial}.", target.Serial);
            await RunOnUiContextAsync(() => SetTargetLog(target, "Log_RandomDeviceFailed"))
                .ConfigureAwait(false);
        }
        finally
        {
            CompleteBatchTarget(target);
        }
    }

    private async Task<bool> CanStartBatchTargetAsync(
        BatchActionTarget target,
        CancellationToken cancellationToken)
    {
        bool canStart = false;
        await RunOnUiContextAsync(() =>
        {
            if (!IsCurrentTarget(target))
                return;

            if (target.Device.ConnectionStatus != AdbDeviceStatus.Online)
            {
                SetTargetLog(target, "Log_DeviceMustBeOnline");
                return;
            }

            canStart = true;
        }).ConfigureAwait(false);

        if (!canStart)
            return false;

        bool isOnline;
        try
        {
            isOnline = await _deviceListService
                .IsDeviceOnlineAsync(target.Serial, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                exception,
                "Live ADB preflight failed for device {Serial}.",
                target.Serial);
            isOnline = false;
        }

        if (isOnline)
            return true;

        await RunOnUiContextAsync(() => SetTargetLog(target, "Log_DeviceMustBeOnline"))
            .ConfigureAwait(false);
        return false;
    }

    [RelayCommand]
    private async Task SaveMultipleDeviceColumnRatiosAsync(
        IReadOnlyDictionary<string, double>? ratios,
        CancellationToken cancellationToken)
    {
        if (ratios == null || ratios.Count == 0)
            return;

        DeviceTableColumnRatioHelper.Replace(_settings.DeviceTableColumnRatios, ratios);

        OnPropertyChanged(nameof(DeviceTableColumnRatios));
        await SaveSettingsAsync(cancellationToken).ConfigureAwait(false);
    }

    [RelayCommand(CanExecute = nameof(CanOpenAdvancedChangeConfig))]
    private async Task OpenAdvancedChangeConfigAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<string> deviceSerials =
            await GetSelectedOnlineSerialsAsync(cancellationToken).ConfigureAwait(true);
        if (deviceSerials.Count == 0 || UseDefaultChangeMode)
            return;

        try
        {
            DeviceChangeOptions optionsSnapshot = DeviceChangeOptionsHelper.CreateNormalizedCopy(
                _changeOptions,
                useDefaultMode: false);
            AdvancedChangeConfigDialogResult? result = deviceSerials.Count == 1
                ? await _advancedChangeConfigDialogService
                    .ShowAdvancedChangeConfigAsync(
                        deviceSerials[0],
                        optionsSnapshot,
                        UseIntegritySecurityPatch,
                        cancellationToken)
                    .ConfigureAwait(true)
                : await _advancedChangeConfigDialogService
                    .ShowAdvancedChangeConfigAsync(
                        deviceSerials,
                        optionsSnapshot,
                        UseIntegritySecurityPatch,
                        isMultiple: true,
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(true);
            if (result == null)
                return;

            _isApplyingConfiguration = true;
            try
            {
                _changeOptions = DeviceChangeOptionsHelper.CreateNormalizedCopy(
                    result.Options,
                    useDefaultMode: false);
                UseIntegritySecurityPatch = result.UseIntegritySecurityPatch;
                UseDefaultChangeMode = false;
            }
            finally
            {
                _isApplyingConfiguration = false;
            }

            QueueConfigurationSave();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Failed to open Advanced Change Config for Multiple Device preset.");
        }
    }

    private bool CanOpenAdvancedChangeConfig()
    {
        return !UseDefaultChangeMode
               && GetSelectedDevicesForPackageConfig().Count > 0;
    }

    private IReadOnlyList<DeviceRowViewModel> GetSelectedDevicesForPackageConfig()
    {
        return _allDeviceRows
            .Where(device => device.IsSelected)
            .ToArray();
    }

    private async Task<IReadOnlyList<string>> GetSelectedOnlineSerialsAsync(
        CancellationToken cancellationToken)
    {
        DeviceRowViewModel[] selected = GetSelectedDevicesForPackageConfig()
            .ToArray();
        if (selected.Length == 0)
            return [];

        bool[] online = await Task.WhenAll(selected.Select(async device =>
        {
            try
            {
                return await _deviceListService
                    .IsDeviceOnlineAsync(device.Serial, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                _logger.LogWarning(
                    exception,
                    "Failed to verify device {Serial} before loading package configuration.",
                    device.Serial);
                return false;
            }
        })).ConfigureAwait(true);

        return selected
            .Where((_, index) => online[index])
            .Select(device => device.Serial)
            .ToArray();
    }

    private async Task LoadCarrierProfilesAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<CarrierProfile> profiles =
            await _carrierDataService
                .GetCarrierProfilesAsync(cancellationToken)
                .ConfigureAwait(false);
        await RunOnUiContextAsync(() =>
        {
            _carrierProfiles = profiles.ToList();
            RefreshCountryOptions();
        }).ConfigureAwait(false);
    }

    private async Task LoadConfigurationAsync(CancellationToken cancellationToken)
    {
        MultipleDeviceConfiguration configuration =
            await _multipleDeviceConfigService
                .LoadAsync(cancellationToken)
                .ConfigureAwait(false);
        await RunOnUiContextAsync(() => ApplyConfiguration(configuration)).ConfigureAwait(false);
    }

    private async Task LoadDevicesAsync(CancellationToken cancellationToken)
    {
        await _deviceRefreshLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            DeviceListSnapshot snapshot =
                await _deviceListService.LoadSnapshotAsync(cancellationToken).ConfigureAwait(false);
            await RunOnUiContextAsync(() => ApplyDeviceListSnapshot(snapshot)).ConfigureAwait(false);
        }
        finally
        {
            _deviceRefreshLock.Release();
        }
    }

    internal void ApplyDeviceListSnapshot(DeviceListSnapshot snapshot)
    {
        _storedDevices = snapshot.StoredDevices.ToList();
        RefreshDeviceRows(snapshot.StoredDevices, snapshot.ConnectedDevices);
    }

    private void RefreshDeviceRows(
        IReadOnlyList<StoredDeviceConfig> storedDevices,
        IReadOnlyList<AdbDevice> connectedDevices)
    {
        HashSet<string> currentSerials = storedDevices
            .Select(device => device.Serial)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (string serial in _randomDeviceProfiles.Keys
                     .Concat(_randomSimProfiles.Keys)
                     .Distinct(StringComparer.OrdinalIgnoreCase)
                     .Where(serial => !currentSerials.Contains(serial))
                     .ToArray())
        {
            _randomDeviceProfiles.Remove(serial);
            _randomSimProfiles.Remove(serial);
        }

        string? selectedInfoSerial = SelectedInfoDevice?.Serial;
        var selectedSerials = _allDeviceRows.Count > 0
            ? _allDeviceRows
                .Where(device => device.IsSelected)
                .Select(device => device.Serial)
                .ToHashSet(StringComparer.OrdinalIgnoreCase)
            : _settings.SelectedMultipleDeviceSerials.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var connectedBySerial = connectedDevices.ToDictionary(
            device => device.Serial,
            StringComparer.OrdinalIgnoreCase);
        var existingRows = _allDeviceRows.ToDictionary(
            device => device.Serial,
            StringComparer.OrdinalIgnoreCase);
        var refreshedRows = new List<DeviceRowViewModel>(storedDevices.Count);

        _isRefreshingRows = true;
        try
        {
            for (int index = 0; index < storedDevices.Count; index++)
            {
                StoredDeviceConfig storedDevice = storedDevices[index];
                connectedBySerial.TryGetValue(storedDevice.Serial, out AdbDevice? connectedDevice);
                AdbDeviceStatus connectionStatus = connectedDevice?.Status ?? AdbDeviceStatus.Offline;
                string statusText = GetConnectionStatusText(connectionStatus);
                if (existingRows.Remove(storedDevice.Serial, out DeviceRowViewModel? row))
                {
                    row.UpdateSnapshot(
                        index + 1,
                        storedDevice.Name,
                        storedDevice.Type,
                        connectionStatus,
                        statusText,
                        IsDeviceBusy(row.Serial));
                    row.RestoreAction(_deviceActionCoordinatorService.GetOperation(row.Serial));
                }
                else
                {
                    row = DeviceRowFactory.CreateDeviceRow(
                        index + 1,
                        storedDevice,
                        connectedDevice,
                        statusText,
                        _localizationService.GetString("Log_Ready"));
                    row.RestoreAction(_deviceActionCoordinatorService.GetOperation(row.Serial));
                    row.PropertyChanged += OnDeviceRowPropertyChanged;
                }

                RestoreSharedProcessState(row);
                row.IsSelected = selectedSerials.Contains(row.Serial);
                refreshedRows.Add(row);
            }

            foreach (DeviceRowViewModel removedRow in existingRows.Values)
                removedRow.PropertyChanged -= OnDeviceRowPropertyChanged;

            _allDeviceRows.Clear();
            _allDeviceRows.AddRange(refreshedRows);

            ApplyDeviceFilterCore();
        }
        finally
        {
            _isRefreshingRows = false;
        }

        InvalidateQueuedBatchTargets(serial =>
            !currentSerials.Contains(serial)
            || !connectedBySerial.TryGetValue(serial, out AdbDevice? connectedDevice)
            || connectedDevice.Status != AdbDeviceStatus.Online);

        SynchronizeSelectedDeviceSettings();
        if (selectedInfoSerial != null)
        {
            DeviceRowViewModel? restoredInfoDevice = SelectedDevices.FirstOrDefault(device =>
                SerialEquals(device.Serial, selectedInfoSerial));
            if (restoredInfoDevice != null)
                SelectedInfoDevice = restoredInfoDevice;
        }
        RefreshRunningActions();
    }

    private void ApplyDeviceFilter()
    {
        _isRefreshingRows = true;
        try
        {
            ApplyDeviceFilterCore();
        }
        finally
        {
            _isRefreshingRows = false;
        }

        NotifySelectionChanged();
    }

    private void RestoreSharedProcessState(DeviceRowViewModel row)
    {
        if (_deviceProcessStateService.Get(row.Serial) is { } process)
        {
            row.RestoreProcess(process.Message, process.State);
            return;
        }

        row.RestoreProcess(GetLogText("Log_Ready"), DeviceProcessState.Ready);
    }

    private void ApplyDeviceFilterCore()
    {
        List<DeviceRowViewModel> visibleDevices = _allDeviceRows
            .Where(MatchesDeviceFilter)
            .ToList();
        for (int index = Devices.Count - 1; index >= 0; index--)
        {
            if (!visibleDevices.Contains(Devices[index]))
                Devices.RemoveAt(index);
        }

        for (int targetIndex = 0; targetIndex < visibleDevices.Count; targetIndex++)
        {
            DeviceRowViewModel device = visibleDevices[targetIndex];
            int currentIndex = Devices.IndexOf(device);
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

    partial void OnDeviceSearchTextChanged(string value)
    {
        ApplyDeviceFilter();
    }

    private void OnDeviceRowPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (_isRefreshingRows || sender is not DeviceRowViewModel deviceRow)
            return;

        if (args.PropertyName == nameof(DeviceRowViewModel.IsSelected))
        {
            if (!_isBatchUpdatingSelection)
                SynchronizeSelectedDeviceSettings();
            return;
        }

        if (args.PropertyName == nameof(DeviceRowViewModel.Name))
        {
            QueueDeviceRowEdit(deviceRow);
            ReapplySearchIfActive();
            RefreshRunningActions();
            return;
        }

        if (args.PropertyName == nameof(DeviceRowViewModel.Type))
        {
            CancelPendingDeviceEdit(deviceRow.Serial);
            TrackSilentSave(
                SaveDeviceRowEditAsync(
                    CreateDeviceRowEditSnapshot(deviceRow),
                    GetActiveToken()),
                "Failed to save Multiple Device row edit.");
            ReapplySearchIfActive();
            return;
        }

        if (args.PropertyName == nameof(DeviceRowViewModel.ConnectionStatus))
        {
            ApplyDeviceFilter();
            NotifySelectionChanged();
            RefreshRunningActions();
        }
    }

    private void OnDeviceActionStateChanged(DeviceActionOperationSnapshot snapshot)
    {
        if (snapshot.State == DeviceActionRuntimeState.Idle)
        {
            TaskCompletionSource? completion = null;
            lock (_activeContextOperationsLock)
            {
                if (_activeContextOperationIds.Remove(snapshot.OperationId)
                    && _activeContextOperationIds.Count == 0)
                {
                    completion = _activeContextOperationsCompletion;
                    _activeContextOperationsCompletion = null;
                }
            }

            completion?.TrySetResult();
        }

        if (_isDisposed)
            return;

        void ApplyBusyState()
        {
            bool effectiveBusy = _deviceActionCoordinatorService.IsBusy(snapshot.Serial);
            foreach (DeviceRowViewModel device in _allDeviceRows
                         .Where(device => SerialEquals(device.Serial, snapshot.Serial)))
            {
                device.RestoreAction(
                    effectiveBusy
                        ? _deviceActionCoordinatorService.GetOperation(snapshot.Serial)
                        : null);
            }

            if (!effectiveBusy
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

            if (SelectedInfoDevice != null
                && SerialEquals(SelectedInfoDevice.Serial, snapshot.Serial))
            {
                OnPropertyChanged(nameof(CanInteractWithSelectedInfoDevice));
                OnPropertyChanged(nameof(CanOperateSelectedInfoDevice));
            }

            NotifySelectionChanged();
            RefreshRunningActions();
        }

        if (_uiDispatcher.CheckAccess())
        {
            ApplyBusyState();
            return;
        }

        TrackSilentSave(
            _uiDispatcher.InvokeAsync(ApplyBusyState),
            "Failed to update Multiple Device action busy state.");
    }

    private void RefreshRunningActions()
    {
        if (_isDisposed)
            return;

        void Apply()
        {
            IReadOnlyList<DeviceActionSessionSnapshot> sessions =
                _deviceActionCoordinatorService.GetActiveSessions() ?? [];
            var activeIds = sessions
                .Select(session => session.SessionId)
                .ToHashSet();
            foreach (Guid sessionId in _runningActionOrder.Keys
                         .Where(sessionId => !activeIds.Contains(sessionId))
                         .ToArray())
            {
                _runningActionOrder.Remove(sessionId);
            }

            foreach (DeviceActionSessionSnapshot session in sessions)
            {
                if (!_runningActionOrder.ContainsKey(session.SessionId))
                    _runningActionOrder[session.SessionId] = _nextRunningActionOrder++;
            }

            for (int index = RunningActions.Count - 1; index >= 0; index--)
            {
                if (!activeIds.Contains(RunningActions[index].SessionId))
                    RunningActions.RemoveAt(index);
            }

            foreach (DeviceActionSessionSnapshot session in sessions
                         .OrderBy(session => _runningActionOrder[session.SessionId]))
            {
                string[] devices = session.Operations
                    .Select(operation => _allDeviceRows.FirstOrDefault(row =>
                        SerialEquals(row.Serial, operation.Serial)) is { } row
                            ? string.IsNullOrWhiteSpace(row.Name)
                                ? row.Serial
                                : $"{row.Name} ({row.Serial})"
                            : operation.Serial)
                    .ToArray();
                RunningActionItemViewModel? current = RunningActions.FirstOrDefault(item =>
                    item.SessionId == session.SessionId);
                if (current == null)
                {
                    RunningActions.Add(new RunningActionItemViewModel(
                        session.SessionId,
                        session.Kind.ToLogicalActionKind(),
                        GetLogText(session.Kind.GetDisplayResourceKey()),
                        devices,
                        session.CanCancel,
                        session.State == DeviceActionRuntimeState.Stopping));
                }
                else
                {
                    current.UpdateDevices(devices);
                    current.CanStop = session.CanCancel;
                    current.IsStopping = session.State == DeviceActionRuntimeState.Stopping;
                }
            }
        }

        if (_uiDispatcher.CheckAccess())
            Apply();
        else
            TrackSilentSave(_uiDispatcher.InvokeAsync(Apply), "Failed to refresh Running Actions.");
    }

    [RelayCommand]
    private void StopRunningAction(RunningActionItemViewModel? action)
    {
        if (action == null || !action.CanStop)
            return;

        _deviceActionCoordinatorService.TryRequestSessionCancellation(action.SessionId);
        RefreshRunningActions();
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
            "Failed to update shared Multiple Device process state.");
    }

    private bool IsDeviceBusy(DeviceRowViewModel device)
    {
        return IsDeviceBusy(device.Serial);
    }

    private bool IsDeviceBusy(string serial)
    {
        return _deviceActionCoordinatorService.IsBusy(serial);
    }

    private DeviceActionOperationSnapshot? GetSelectedInfoDeviceOperation()
    {
        return SelectedInfoDevice == null
            ? null
            : _deviceActionCoordinatorService.GetOperation(SelectedInfoDevice.Serial);
    }

    private DeviceActionOperationSnapshot? GetSelectedInfoDeviceBatchOperation()
    {
        DeviceActionOperationSnapshot? operation = GetSelectedInfoDeviceOperation();
        if (operation == null || !operation.Kind.IsBatchAction())
            return null;

        lock (_activeBatchTargetsLock)
        {
            return _activeBatchTargets.Any(target =>
                       SerialEquals(target.Serial, operation.Serial)
                       && target.Operation.OperationId == operation.OperationId)
                ? operation
                : null;
        }
    }

    private string GetExternalSelectedDeviceActionText()
    {
        DeviceActionOperationSnapshot? operation = GetSelectedInfoDeviceOperation();
        if (operation == null || operation.Kind.IsBatchAction())
            return string.Empty;

        string format = GetLogText(operation.State == DeviceActionRuntimeState.Stopping
            ? "ChangeMultipleDevices_ExternalActionStoppingFormat"
            : "ChangeMultipleDevices_ExternalActionRunningFormat");
        return string.Format(
            format,
            GetLogText(operation.Kind.GetDisplayResourceKey()));
    }

    private string GetLogText(string resourceKey)
    {
        return _localizationService.GetString(resourceKey);
    }

    private void SetDeviceLog(
        DeviceRowViewModel device,
        string resourceKey,
        params object[] formatArguments)
    {
        string template = GetLogText(resourceKey);
        string message = formatArguments.Length == 0
            ? template
            : string.Format(template, formatArguments);
        _deviceProcessStateService.SetProcess(device.Serial, message, resourceKey);
        _logger.LogInformation("Multiple Device {Serial} action: {Message}", device.Serial, message);
    }

    private void ShowBusyActionLog(DeviceRowViewModel device)
    {
        DeviceActionOperationSnapshot? operation =
            _deviceActionCoordinatorService.GetOperation(device.Serial);
        if (operation == null)
            return;

        string message = string.Format(
            GetLogText("Log_DeviceActionAlreadyRunningFormat"),
            GetLogText(operation.Kind.GetDisplayResourceKey()));
        if (_deviceProcessStateService.Get(device.Serial) == null)
        {
            _deviceProcessStateService.SetProcess(
                device.Serial,
                GetLogText("Log_Ready"),
                "Log_Ready");
        }
        _deviceProcessStateService.ShowTemporaryProcess(
            device.Serial,
            message,
            "Log_DeviceActionAlreadyRunningFormat",
            BusyActionLogDuration);
        _logger.LogInformation("Multiple Device {Serial} action attempt while {Action} is running.",
            device.Serial,
            operation.Kind);
    }

    private void SetTargetLog(
        BatchActionTarget target,
        string resourceKey,
        params object[] formatArguments)
    {
        if (IsCurrentTarget(target) && IsActiveBatchTarget(target))
            SetDeviceLog(target.Device, resourceKey, formatArguments);
    }

    private void SetContextOperationCancellationLog(
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

    private void SetContextDialogDismissalLog(
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

        SetContextOperationCancellationLog(device, operation, canceledLogKey);
    }

    private Task SetTargetCancellationResultAsync(
        BatchActionTarget target,
        string userStopLogKey)
    {
        return RunOnUiContextAsync(() =>
        {
            if (target.IsInvalidated)
                return;

            if (target.Device.ConnectionStatus != AdbDeviceStatus.Online)
            {
                SetTargetLog(target, "Log_DeviceMustBeOnline");
                return;
            }

            SetTargetLog(
                target,
                target.Operation.CancellationReason == DeviceActionCancellationReason.UserStop
                    ? userStopLogKey
                    : "Log_Ready");
        });
    }

    private async Task SetBatchCancellationResultsAsync(
        IEnumerable<BatchActionTarget> targets,
        string userStopLogKey)
    {
        foreach (BatchActionTarget target in targets)
        {
            await SetTargetCancellationResultAsync(target, userStopLogKey)
                .ConfigureAwait(false);
        }
    }

    private Task SetTargetDialogDismissalResultAsync(
        BatchActionTarget target,
        string canceledLogKey)
    {
        if (target.Operation.CancellationReason != DeviceActionCancellationReason.None
            || target.Device.ConnectionStatus != AdbDeviceStatus.Online
            || target.IsInvalidated)
        {
            return SetTargetCancellationResultAsync(target, canceledLogKey);
        }

        return RunOnUiContextAsync(() => SetTargetLog(target, canceledLogKey));
    }

    private async Task SetBatchDialogDismissalResultsAsync(
        IEnumerable<BatchActionTarget> targets,
        string canceledLogKey)
    {
        foreach (BatchActionTarget target in targets)
        {
            await SetTargetDialogDismissalResultAsync(target, canceledLogKey)
                .ConfigureAwait(false);
        }
    }

    private bool IsActiveBatchTarget(BatchActionTarget target)
    {
        lock (_activeBatchTargetsLock)
            return _activeBatchTargets.Contains(target);
    }

    private bool IsCurrentTarget(BatchActionTarget target)
    {
        return _allDeviceRows.Any(device => ReferenceEquals(device, target.Device));
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
            DisplayDeviceInfo(null);
        }
    }

    private void ApplyRandomDeviceInfo(BatchActionTarget target, DeviceInfoApiDevice randomDevice)
    {
        if (!IsCurrentTarget(target))
            return;

        string serial = target.Serial;
        _randomDeviceProfiles[serial] = randomDevice;
        SimProfile? simProfile = CreateSimProfile(randomDevice);
        if (simProfile == null)
            _randomSimProfiles.Remove(serial);
        else
            _randomSimProfiles[serial] = simProfile;

        if (SelectedInfoDevice != null && SerialEquals(SelectedInfoDevice.Serial, serial))
            DisplayDeviceInfo(randomDevice);

        ViewRandomDeviceInfoCommand.NotifyCanExecuteChanged();
    }

    private void DisplayDeviceInfo(DeviceInfoApiDevice? randomDevice)
    {
        SynchronizeDeviceInfo(() =>
        {
            DeviceInfo.Name = GetFirstValue(randomDevice?.Name, randomDevice?.Board, randomDevice?.Code);
            DeviceInfo.Hardware = randomDevice?.Hardware ?? string.Empty;
            DeviceInfo.Fingerprint = randomDevice?.Fingerprint ?? string.Empty;
            DeviceInfo.Model = randomDevice?.Model ?? string.Empty;
            DeviceInfo.Brand = GetFirstValue(randomDevice?.Brand, randomDevice?.Manufacturer);
            DeviceInfo.AndroidVersion = GetAndroidVersionDisplay(randomDevice?.Release, randomDevice?.Sdk);
            DeviceInfo.Serial = randomDevice?.Serial ?? string.Empty;
            DeviceInfo.Imei = randomDevice?.Imei ?? string.Empty;
            DeviceInfo.Iccid = randomDevice?.Iccid ?? string.Empty;
            DeviceInfo.Imsi = randomDevice?.Imsi ?? string.Empty;
            DeviceInfo.Operator = string.IsNullOrWhiteSpace(randomDevice?.SimOperatorName)
                ? randomDevice?.SimOperatorNumeric ?? string.Empty
                : randomDevice.SimOperatorName;
            DeviceInfo.PhoneNumber = randomDevice?.SimPhoneNumber ?? string.Empty;
            DeviceInfo.Mac = randomDevice?.WifiMacAddress ?? string.Empty;
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
        if (_isSynchronizingDeviceInfo
            || SelectedInfoDevice == null
            || IsDeviceBusy(SelectedInfoDevice)
            || !_randomDeviceProfiles.TryGetValue(SelectedInfoDevice.Serial, out DeviceInfoApiDevice? profile))
        {
            return;
        }

        CopyFormValuesToProfile(profile);
    }

    private void ApplyRandomSimInfo(BatchActionTarget target, SimProfile simProfile)
    {
        if (!IsCurrentTarget(target))
            return;

        string serial = target.Serial;
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

        if (SelectedInfoDevice == null || !SerialEquals(SelectedInfoDevice.Serial, serial))
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

    private static SimProfile CreateEditedSimProfile(
        DeviceInfoApiDevice? deviceProfile,
        SimProfile profile)
    {
        if (deviceProfile == null)
            return profile;

        return new SimProfile
        {
            Iccid = deviceProfile.Iccid.Trim(),
            Imsi = deviceProfile.Imsi.Trim(),
            PhoneNumber = deviceProfile.SimPhoneNumber.Trim(),
            OperatorName = deviceProfile.SimOperatorName.Trim(),
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
            OperatorNumeric = profile.SimOperatorNumeric,
            OperatorCountry = profile.SimOperatorCountry,
            OperatorName = profile.SimOperatorName
        };
    }

    private void SynchronizeSelectedDeviceSettings()
    {
        List<string> selectedSerials = _allDeviceRows
            .Where(device => device.IsSelected)
            .Select(device => device.Serial)
            .ToList();
        bool changed = !_settings.SelectedMultipleDeviceSerials.SequenceEqual(
            selectedSerials,
            StringComparer.OrdinalIgnoreCase);
        _settings.SelectedMultipleDeviceSerials = selectedSerials;
        if (changed)
            QueueSettingsSave();

        SynchronizeSelectedDevices(selectedSerials);

        NotifySelectionChanged();
    }

    private void SynchronizeSelectedDevices(IReadOnlyList<string> selectedSerials)
    {
        var selectedRows = selectedSerials
            .Select(serial => _allDeviceRows.FirstOrDefault(device => SerialEquals(device.Serial, serial)))
            .Where(device => device != null)
            .Cast<DeviceRowViewModel>()
            .ToArray();

        for (int index = SelectedDevices.Count - 1; index >= 0; index--)
        {
            if (!selectedRows.Contains(SelectedDevices[index]))
                SelectedDevices.RemoveAt(index);
        }

        for (int index = 0; index < selectedRows.Length; index++)
        {
            DeviceRowViewModel row = selectedRows[index];
            int currentIndex = SelectedDevices.IndexOf(row);
            if (currentIndex < 0)
                SelectedDevices.Insert(index, row);
            else if (currentIndex != index)
                SelectedDevices.Move(currentIndex, index);
        }

        DeviceRowViewModel? current = SelectedInfoDevice;
        if (current == null || !selectedRows.Contains(current))
            SelectedInfoDevice = selectedRows.FirstOrDefault();
    }

    private void NotifySelectionChanged()
    {
        OnPropertyChanged(nameof(AllDevicesSelectionState));
        OnPropertyChanged(nameof(CanInteractWithSelectedInfoDevice));
        OnPropertyChanged(nameof(CanOperateSelectedInfoDevice));
        OnPropertyChanged(nameof(DisplayedSelectedDeviceActionKind));
        OnPropertyChanged(nameof(HasExternalSelectedDeviceAction));
        OnPropertyChanged(nameof(ExternalSelectedDeviceActionText));
        NotifyBatchPresentationChanged();
        AddNewDevicesCommand.NotifyCanExecuteChanged();
        OpenAdvancedChangeConfigCommand.NotifyCanExecuteChanged();
        NotifyBatchActionCanExecuteChanged();
        ViewRandomDeviceInfoCommand.NotifyCanExecuteChanged();
        DeleteDeviceCommand.NotifyCanExecuteChanged();
    }

    private void NotifyBatchPresentationChanged()
    {
        OnPropertyChanged(nameof(SelectedBatchActionKind));
        OnPropertyChanged(nameof(IsSelectedInfoDeviceActiveBatchTarget));
        OnPropertyChanged(nameof(HasSelectedInfoDeviceBatchStopButton));
        OnPropertyChanged(nameof(ShowSelectedDeviceBatchStop));
        OnPropertyChanged(nameof(HasActiveBatchActionButton));
        OnPropertyChanged(nameof(CanStopSelectedDeviceAction));
        OnPropertyChanged(nameof(SelectedBatchActionButtonRow));
        OnPropertyChanged(nameof(SelectedBatchActionButtonColumn));
        OnPropertyChanged(nameof(BatchActionStopText));
    }

    private void NotifyBatchActionCanExecuteChanged()
    {
        RandomSelectedDevicesCommand.NotifyCanExecuteChanged();
        ChangeSelectedDevicesCommand.NotifyCanExecuteChanged();
        RandomChangeAndWipeSelectedDevicesCommand.NotifyCanExecuteChanged();
        ChangeSelectedDevicesWithoutWipeCommand.NotifyCanExecuteChanged();
        WipeSelectedDevicesWithoutChangeCommand.NotifyCanExecuteChanged();
        RandomSelectedSimsCommand.NotifyCanExecuteChanged();
        ChangeSelectedSimsCommand.NotifyCanExecuteChanged();
        ChangeSelectedLocationsCommand.NotifyCanExecuteChanged();
        ChangeSelectedTimezonesCommand.NotifyCanExecuteChanged();
        InstallSelectedPackagesCommand.NotifyCanExecuteChanged();
        StopSelectedDeviceActionCommand.NotifyCanExecuteChanged();
        StopDeviceActionCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanStopSelectedDeviceActionCommandCanExecute))]
    private void StopSelectedDeviceAction()
    {
        DeviceRowViewModel? device = SelectedInfoDevice;
        if (device == null || !CanStopSelectedDeviceAction)
            return;

        _deviceActionCoordinatorService.TryRequestCancellation(device.Serial);
        NotifySelectionChanged();
    }

    private bool CanStopSelectedDeviceActionCommandCanExecute()
    {
        return CanStopSelectedDeviceAction;
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

    private async Task RefreshNewDeviceCountAsync(CancellationToken cancellationToken)
    {
        if (!await _deviceRefreshLock.WaitAsync(0, cancellationToken).ConfigureAwait(false))
            return;

        try
        {
            DeviceListSnapshot snapshot =
                await _deviceListService.LoadSnapshotAsync(cancellationToken).ConfigureAwait(false);
            int newDeviceCount = _deviceListService.CountNewDevices(
                snapshot.StoredDevices,
                snapshot.ConnectedDevices);
            await RunOnUiContextAsync(() =>
            {
                bool deviceListChanged = !HaveSameDeviceSerials(
                    _storedDevices,
                    snapshot.StoredDevices);
                _storedDevices = snapshot.StoredDevices.ToList();
                if (deviceListChanged)
                    RefreshDeviceRows(snapshot.StoredDevices, snapshot.ConnectedDevices);
                else
                    UpdateDeviceConnectionStatuses(snapshot.ConnectedDevices);

                NewDeviceCountText = FormatCount(
                    "ChangeMultipleDevices_NewDeviceCount",
                    newDeviceCount);
            }).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Failed to refresh Multiple Device status.");
            await RunOnUiContextAsync(() =>
            {
                NewDeviceCountText = FormatCount(
                    "ChangeMultipleDevices_NewDeviceCount",
                    _localizationService.GetString("ChangeMultipleDevices_NotAvailable"));
            }).ConfigureAwait(false);
        }
        finally
        {
            _deviceRefreshLock.Release();
        }
    }

    private void UpdateDeviceConnectionStatuses(IReadOnlyList<AdbDevice> connectedDevices)
    {
        var connectedBySerial = connectedDevices.ToDictionary(
            device => device.Serial,
            StringComparer.OrdinalIgnoreCase);
        var wasRefreshingRows = _isRefreshingRows;
        _isRefreshingRows = true;
        try
        {
            foreach (DeviceRowViewModel device in _allDeviceRows)
            {
                device.ConnectionStatus =
                    connectedBySerial.TryGetValue(device.Serial, out AdbDevice? connectedDevice)
                        ? connectedDevice.Status
                        : AdbDeviceStatus.Offline;
                device.Status = GetConnectionStatusText(device.ConnectionStatus);
            }
        }
        finally
        {
            _isRefreshingRows = wasRefreshingRows;
        }

        InvalidateQueuedBatchTargets(serial =>
            !connectedBySerial.TryGetValue(serial, out AdbDevice? connectedDevice)
            || connectedDevice.Status != AdbDeviceStatus.Online);

        ApplyDeviceFilter();
        NotifySelectionChanged();
    }

    private string GetConnectionStatusText(AdbDeviceStatus status)
    {
        string resourceKey = status switch
        {
            AdbDeviceStatus.Online => "ChangeMultipleDevices_StatusOnline",
            AdbDeviceStatus.Unauthorized => "ChangeMultipleDevices_StatusUnauthorized",
            _ => "ChangeMultipleDevices_StatusOffline"
        };
        return _localizationService.GetString(resourceKey);
    }

    private void ApplyConfiguration(MultipleDeviceConfiguration configuration)
    {
        _isApplyingConfiguration = true;
        try
        {
            MultipleDeviceChangeConfig changeConfig = configuration.ChangeConfig;
            SelectedBrand = FindOption(Brands, changeConfig.Brand) ?? "Random";
            UpdateAndroidVersionOptions(SelectedBrand, changeConfig.AndroidVersion);
            SelectedModel = changeConfig.Model;
            IsChangeSimEnabled = changeConfig.ChangeSimEnabled;
            UseIntegritySecurityPatch = changeConfig.UseIntegritySecurityPatch;
            _changeOptions = DeviceChangeOptionsHelper.CreateNormalizedCopy(
                configuration.ChangeOptions);
            UseDefaultChangeMode = _changeOptions.UseDefaultMode;

            CarrierCountryOption? selectedCountry =
                FindCountryOption(changeConfig.CountryIso)
                ?? FindCountryOptionByName(changeConfig.CountryName)
                ?? FindCountryOption(DefaultCountryIso)
                ?? Countries.FirstOrDefault();
            SelectedCountry = selectedCountry;
            UpdateCarrierOptionsForCountry(selectedCountry?.CountryIso, changeConfig);
        }
        finally
        {
            _isApplyingConfiguration = false;
        }

        OpenAdvancedChangeConfigCommand.NotifyCanExecuteChanged();
    }

    private MultipleDeviceConfiguration CreateConfiguration()
    {
        return new MultipleDeviceConfiguration
        {
            ChangeConfig = new MultipleDeviceChangeConfig
            {
                Brand = SelectedBrand ?? string.Empty,
                AndroidVersion = SelectedAndroidVersion ?? string.Empty,
                Model = SelectedModel,
                CountryIso = SelectedCountry?.CountryIso ?? string.Empty,
                CountryName = SelectedCountry?.CountryName ?? string.Empty,
                Carrier = SelectedCarrier?.CarrierName ?? string.Empty,
                CarrierMcc = SelectedCarrier?.Mcc ?? string.Empty,
                CarrierMnc = SelectedCarrier?.Mnc ?? string.Empty,
                ChangeSimEnabled = IsChangeSimEnabled,
                UseIntegritySecurityPatch = UseIntegritySecurityPatch
            },
            ChangeOptions = DeviceChangeOptionsHelper.CreateNormalizedCopy(
                _changeOptions,
                UseDefaultChangeMode)
        };
    }

    private void RefreshCountryOptions()
    {
        Countries.Clear();
        foreach (CarrierProfile profile in _carrierProfiles
                     .GroupBy(item => item.CountryIso, StringComparer.OrdinalIgnoreCase)
                     .Select(group => group.First())
                     .OrderBy(item => item.CountryName, StringComparer.OrdinalIgnoreCase)
                     .ThenBy(item => item.CountryIso, StringComparer.OrdinalIgnoreCase))
        {
            Countries.Add(
                new CarrierCountryOption(
                    profile.CountryIso,
                    profile.CountryCode,
                    profile.CountryName));
        }
    }

    private void UpdateCarrierOptionsForCountry(
        string? countryIso,
        MultipleDeviceChangeConfig? changeConfig)
    {
        _isUpdatingCarrierOptions = true;
        try
        {
            Carriers.Clear();
            string targetCountryIso = string.IsNullOrWhiteSpace(countryIso)
                ? DefaultCountryIso
                : countryIso.Trim().ToLowerInvariant();
            foreach (CarrierOption carrier in _carrierProfiles
                         .Where(profile => SerialEquals(profile.CountryIso, targetCountryIso))
                         .Select(profile => new CarrierOption(
                             profile.CarrierName,
                             profile.Mcc,
                             profile.Mnc))
                         .OrderBy(item => item.CarrierName, StringComparer.OrdinalIgnoreCase)
                         .ThenBy(item => item.Mcc, StringComparer.OrdinalIgnoreCase)
                         .ThenBy(item => item.Mnc, StringComparer.OrdinalIgnoreCase))
            {
                Carriers.Add(carrier);
            }

            SelectedCarrier = FindCarrierOption(changeConfig) ?? Carriers.FirstOrDefault();
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
            : Countries.FirstOrDefault(country =>
                SerialEquals(country.CountryIso, countryIso.Trim()));
    }

    private CarrierCountryOption? FindCountryOptionByName(string? countryName)
    {
        return string.IsNullOrWhiteSpace(countryName)
            ? null
            : Countries.FirstOrDefault(country =>
                string.Equals(
                    country.CountryName,
                    countryName.Trim(),
                    StringComparison.OrdinalIgnoreCase));
    }

    private CarrierOption? FindCarrierOption(MultipleDeviceChangeConfig? changeConfig)
    {
        if (changeConfig == null || string.IsNullOrWhiteSpace(changeConfig.Carrier))
            return null;

        return Carriers.FirstOrDefault(carrier =>
                   string.Equals(
                       carrier.CarrierName,
                       changeConfig.Carrier,
                       StringComparison.OrdinalIgnoreCase)
                   && string.Equals(
                       carrier.Mcc,
                       changeConfig.CarrierMcc,
                       StringComparison.OrdinalIgnoreCase)
                   && string.Equals(
                       carrier.Mnc,
                       changeConfig.CarrierMnc,
                       StringComparison.OrdinalIgnoreCase))
               ?? Carriers.FirstOrDefault(carrier =>
                   string.Equals(
                       carrier.CarrierName,
                       changeConfig.Carrier,
                       StringComparison.OrdinalIgnoreCase));
    }

    private static string? FindOption(IReadOnlyList<string> options, string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? null
            : options.FirstOrDefault(option =>
                string.Equals(option, value.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    private void UpdateAndroidVersionOptions(string? brand, string? preferredVersion)
    {
        AndroidVersions.Clear();
        foreach (string version in DeviceProfileOptionsHelper.GetAndroidVersions(brand))
            AndroidVersions.Add(version);

        SelectedAndroidVersion =
            FindOption(AndroidVersions, preferredVersion) ?? "Random";
    }

    private void QueueConfigurationSave()
    {
        if (_isApplyingConfiguration)
            return;

        var cancellation = new CancellationTokenSource();
        lock (_pendingConfigSaveLock)
        {
            _configSaveCancellation?.Cancel();
            _configSaveCancellation = cancellation;
            MultipleDeviceConfiguration snapshot = CreateConfiguration();
            _configSaveTask = PersistConfigurationAfterDelayAsync(snapshot, cancellation);
        }
    }

    private async Task PersistConfigurationAfterDelayAsync(
        MultipleDeviceConfiguration configuration,
        CancellationTokenSource cancellation)
    {
        try
        {
            await Task.Delay(
                    SaveDebounceMilliseconds,
                    cancellation.Token)
                .ConfigureAwait(false);
            await _multipleDeviceConfigService
                .SaveAsync(configuration, cancellation.Token)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Failed to save Multiple Device configuration.");
        }
        finally
        {
            lock (_pendingConfigSaveLock)
            {
                if (ReferenceEquals(_configSaveCancellation, cancellation))
                {
                    _configSaveCancellation = null;
                    _configSaveTask = Task.CompletedTask;
                }
            }

            cancellation.Dispose();
        }
    }

    private async Task FlushPendingConfigurationSaveAsync()
    {
        MultipleDeviceConfiguration flushSnapshot = CreateConfiguration();
        Task pendingTask;
        CancellationTokenSource? cancellation;
        lock (_pendingConfigSaveLock)
        {
            pendingTask = _configSaveTask;
            cancellation = _configSaveCancellation;
            _configSaveCancellation = null;
            _configSaveTask = Task.CompletedTask;
            cancellation?.Cancel();
        }

        await pendingTask.ConfigureAwait(false);
        if (cancellation != null)
        {
            try
            {
                await _multipleDeviceConfigService
                    .SaveAsync(flushSnapshot, CancellationToken.None)
                    .ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                _logger.LogError(
                    exception,
                    "Failed to flush Multiple Device configuration.");
            }
        }
    }

    private void QueueSettingsSave()
    {
        var cancellation = new CancellationTokenSource();
        lock (_pendingSettingsSaveLock)
        {
            _settingsSaveCancellation?.Cancel();
            _settingsSaveCancellation = cancellation;
            _settingsSaveTask = PersistSettingsAfterDelayAsync(cancellation);
        }
    }

    private async Task PersistSettingsAfterDelayAsync(
        CancellationTokenSource cancellation)
    {
        try
        {
            await Task.Delay(
                    SaveDebounceMilliseconds,
                    cancellation.Token)
                .ConfigureAwait(false);
            await SaveSettingsAsync(cancellation.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
        finally
        {
            lock (_pendingSettingsSaveLock)
            {
                if (ReferenceEquals(_settingsSaveCancellation, cancellation))
                {
                    _settingsSaveCancellation = null;
                    _settingsSaveTask = Task.CompletedTask;
                }
            }

            cancellation.Dispose();
        }
    }

    private async Task FlushPendingSettingsSaveAsync()
    {
        Task pendingTask;
        CancellationTokenSource? cancellation;
        lock (_pendingSettingsSaveLock)
        {
            pendingTask = _settingsSaveTask;
            cancellation = _settingsSaveCancellation;
            _settingsSaveCancellation = null;
            _settingsSaveTask = Task.CompletedTask;
            cancellation?.Cancel();
        }

        await pendingTask.ConfigureAwait(false);
        if (cancellation != null)
            await SaveSettingsAsync(CancellationToken.None).ConfigureAwait(false);
    }

    private void QueueDeviceRowEdit(DeviceRowViewModel deviceRow)
    {
        var cancellation = new CancellationTokenSource();
        var pendingEdit = new PendingDeviceEdit(
            CreateDeviceRowEditSnapshot(deviceRow),
            cancellation);
        lock (_pendingDeviceEditsLock)
        {
            if (_pendingDeviceEdits.Remove(deviceRow.Serial, out PendingDeviceEdit? previous))
                previous.Cancellation.Cancel();

            _pendingDeviceEdits[deviceRow.Serial] = pendingEdit;
            pendingEdit.PersistenceTask = PersistDeviceRowAfterDelayAsync(pendingEdit);
        }
    }

    private DeviceRowEditSnapshot CreateDeviceRowEditSnapshot(DeviceRowViewModel deviceRow)
    {
        return new DeviceRowEditSnapshot(
            deviceRow.Serial,
            deviceRow.Name,
            deviceRow.Type,
            Country: null,
            Carrier: null,
            IncludeSelectedCarrierConfig: false);
    }

    private void CancelPendingDeviceEdit(string serial)
    {
        lock (_pendingDeviceEditsLock)
        {
            if (_pendingDeviceEdits.Remove(serial, out PendingDeviceEdit? pendingEdit))
                pendingEdit.Cancellation.Cancel();
        }
    }

    private async Task PersistDeviceRowAfterDelayAsync(PendingDeviceEdit pendingEdit)
    {
        try
        {
            await Task.Delay(
                    SaveDebounceMilliseconds,
                    pendingEdit.Cancellation.Token)
                .ConfigureAwait(false);
            await SaveDeviceRowEditAsync(
                    pendingEdit.Snapshot,
                    pendingEdit.Cancellation.Token)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (
            pendingEdit.Cancellation.IsCancellationRequested)
        {
        }
        finally
        {
            lock (_pendingDeviceEditsLock)
            {
                if (_pendingDeviceEdits.TryGetValue(
                        pendingEdit.Snapshot.Serial,
                        out PendingDeviceEdit? current)
                    && ReferenceEquals(current, pendingEdit))
                {
                    _pendingDeviceEdits.Remove(pendingEdit.Snapshot.Serial);
                }
            }

            pendingEdit.Cancellation.Dispose();
        }
    }

    private async Task FlushPendingDeviceEditsAsync()
    {
        PendingDeviceEdit[] pendingEdits;
        lock (_pendingDeviceEditsLock)
        {
            pendingEdits = _pendingDeviceEdits.Values.ToArray();
            _pendingDeviceEdits.Clear();
            foreach (PendingDeviceEdit pendingEdit in pendingEdits)
                pendingEdit.Cancellation.Cancel();
        }

        if (pendingEdits.Length == 0)
            return;

        await Task.WhenAll(pendingEdits.Select(edit => edit.PersistenceTask)).ConfigureAwait(false);
        foreach (PendingDeviceEdit pendingEdit in pendingEdits)
        {
            await SaveDeviceRowEditAsync(
                    pendingEdit.Snapshot,
                    CancellationToken.None)
                .ConfigureAwait(false);
        }
    }

    private async Task SaveDeviceRowEditAsync(
        DeviceRowEditSnapshot snapshot,
        CancellationToken cancellationToken)
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

    private async Task SaveSettingsAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _settingsService.SaveAsync(_settings, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Failed to save Multiple Device settings.");
        }
    }

    private bool CanViewRandomDeviceInfo()
    {
        // Full device information is intentionally available only from the
        // Single Device screen; Multiple uses Running Actions here.
        return false;
    }

    [RelayCommand(CanExecute = nameof(CanViewRandomDeviceInfo), AllowConcurrentExecutions = true)]
    private Task ViewRandomDeviceInfoAsync()
    {
        // Full device information is intentionally available only from the
        // Single Device screen. Multiple Device exposes Running Actions here.
        return Task.CompletedTask;
    }

    private RandomDeviceRequest CreateCurrentRandomDeviceRequest()
    {
        return new RandomDeviceRequest
        {
            SelectedBrand = SelectedBrand,
            SelectedAndroidVersion = SelectedAndroidVersion,
            UseIntegritySecurityPatch = UseDefaultChangeMode || UseIntegritySecurityPatch,
            Country = CloneCountryOption(SelectedCountry),
            Carrier = CloneCarrierOption(SelectedCarrier)
        };
    }

    private static RandomDeviceRequest CreateRandomDeviceRequest(
        MultipleDeviceConfiguration configuration)
    {
        return new RandomDeviceRequest
        {
            SelectedBrand = configuration.ChangeConfig.Brand,
            SelectedAndroidVersion = configuration.ChangeConfig.AndroidVersion,
            UseIntegritySecurityPatch = configuration.ChangeConfig.UseIntegritySecurityPatch
                || configuration.ChangeOptions.UseDefaultMode,
            Country = CreateCountryOption(configuration.ChangeConfig),
            Carrier = CreateCarrierOption(configuration.ChangeConfig)
        };
    }

    private static CarrierCountryOption? CreateCountryOption(MultipleDeviceChangeConfig config)
    {
        return string.IsNullOrWhiteSpace(config.CountryIso)
            && string.IsNullOrWhiteSpace(config.CountryName)
            ? null
            : new CarrierCountryOption(config.CountryIso, string.Empty, config.CountryName);
    }

    private static CarrierOption? CreateCarrierOption(MultipleDeviceChangeConfig config)
    {
        return string.IsNullOrWhiteSpace(config.Carrier)
            ? null
            : new CarrierOption(config.Carrier, config.CarrierMcc, config.CarrierMnc);
    }

    private static RandomDeviceRequest CreateRandomDeviceRequestCopy(RandomDeviceRequest request)
    {
        return new RandomDeviceRequest
        {
            SelectedBrand = request.SelectedBrand,
            SelectedAndroidVersion = request.SelectedAndroidVersion,
            UseIntegritySecurityPatch = request.UseIntegritySecurityPatch,
            Country = CloneCountryOption(request.Country),
            Carrier = CloneCarrierOption(request.Carrier)
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

    private static ChangeLocationDialogResult? CloneLocationDialogResult(
        ChangeLocationDialogResult? result)
    {
        if (result == null)
            return null;

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

    private static ChangeTimezoneDialogResult? CloneTimezoneDialogResult(
        ChangeTimezoneDialogResult? result)
    {
        return result == null
            ? null
            : new ChangeTimezoneDialogResult(result.Mode, result.Timezone);
    }

    private static InstallPackageBatchRequest CloneInstallPackageBatchRequest(
        InstallPackageBatchRequest request)
    {
        return new InstallPackageBatchRequest(
            request.FilePaths.ToArray(),
            new InstallPackageOptions(
                request.Options.GrantPermissions,
                request.Options.AllowDowngrade));
    }

    private void RegisterBatchTarget(BatchActionTarget target)
    {
        lock (_activeBatchTargetsLock)
            _activeBatchTargets.Add(target);

        NotifySelectionChanged();
    }

    private static Task StartBatchTargetWorker(BatchActionTarget target, Func<Task> worker)
    {
        target.TransferCompletionOwnershipToWorker();
        return worker();
    }

    private void CompleteBatchOwnedTargets(IEnumerable<BatchActionTarget> targets)
    {
        foreach (BatchActionTarget target in targets)
        {
            if (!target.IsCompletionOwnedByWorker)
                CompleteBatchTarget(target);
        }
    }

    private void CompleteBatchTarget(BatchActionTarget target)
    {
        lock (_activeBatchTargetsLock)
            _activeBatchTargets.Remove(target);

        target.Dispose();
        RefreshDeviceBusyState(target.Serial);
    }

    private void RefreshDeviceBusyState(string serial)
    {
        void ApplyBusyState()
        {
            bool isBusy = _deviceActionCoordinatorService.IsBusy(serial);
            foreach (DeviceRowViewModel device in _allDeviceRows
                         .Where(device => SerialEquals(device.Serial, serial)))
            {
                device.RestoreAction(
                    isBusy
                        ? _deviceActionCoordinatorService.GetOperation(serial)
                        : null);
            }

            if (SelectedInfoDevice != null
                && SerialEquals(SelectedInfoDevice.Serial, serial))
            {
                OnPropertyChanged(nameof(CanInteractWithSelectedInfoDevice));
                OnPropertyChanged(nameof(CanOperateSelectedInfoDevice));
            }

            NotifySelectionChanged();
            RefreshRunningActions();
        }

        if (_uiDispatcher.CheckAccess())
        {
            ApplyBusyState();
            return;
        }

        TrackSilentSave(
            _uiDispatcher.InvokeAsync(ApplyBusyState),
            "Failed to refresh Multiple Device action busy state.");
    }

    private void InvalidateQueuedBatchTargets(Func<string, bool> shouldInvalidate)
    {
        var invalidatedTargets = new List<BatchActionTarget>();
        lock (_activeBatchTargetsLock)
        {
            foreach (BatchActionTarget target in _activeBatchTargets.ToArray())
            {
                if (!shouldInvalidate(target.Serial) || !target.TryInvalidateQueued())
                    continue;

                _activeBatchTargets.Remove(target);
                invalidatedTargets.Add(target);
            }
        }

        foreach (BatchActionTarget target in invalidatedTargets)
        {
            target.CancelQueuedExecution();
            if (IsCurrentTarget(target) && target.Device.ConnectionStatus != AdbDeviceStatus.Online)
                SetDeviceLog(target.Device, "Log_DeviceMustBeOnline");
        }

        if (invalidatedTargets.Count > 0)
            NotifySelectionChanged();
    }

    private static DeviceInfoApiDevice? CloneDeviceProfile(DeviceInfoApiDevice? profile)
    {
        return profile?.Clone();
    }

    private static SimProfile? CloneSimProfile(SimProfile? profile)
    {
        if (profile == null)
            return null;

        return new SimProfile
        {
            Iccid = profile.Iccid,
            Imsi = profile.Imsi,
            PhoneNumber = profile.PhoneNumber,
            OperatorName = profile.OperatorName,
            OperatorCountry = profile.OperatorCountry,
            OperatorNumeric = profile.OperatorNumeric
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

    private static string GetAndroidVersionDisplay(string? release, string? sdk)
    {
        if (!string.IsNullOrWhiteSpace(release))
        {
            return release.StartsWith("Android ", StringComparison.OrdinalIgnoreCase)
                ? release
                : string.Concat("Android ", release.Trim());
        }

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

    private string FormatCount(string resourceKey, object value)
    {
        return string.Format(_localizationService.GetString(resourceKey), value);
    }

    private static bool SerialEquals(string left, string right)
    {
        return string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
    }

    private static bool HaveSameDeviceSerials(
        IReadOnlyCollection<StoredDeviceConfig> left,
        IReadOnlyCollection<StoredDeviceConfig> right)
    {
        return left.Count == right.Count
               && left
                   .Select(device => device.Serial)
                   .ToHashSet(StringComparer.OrdinalIgnoreCase)
                   .SetEquals(right.Select(device => device.Serial));
    }

    public void Dispose()
    {
        if (_isDisposed)
            return;

        _actionLifetimeCancellation.Cancel();
        CancelBatchWorkflowsAsync().GetAwaiter().GetResult();
        FlushPendingDeviceEditsAsync().GetAwaiter().GetResult();
        FlushPendingConfigurationSaveAsync().GetAwaiter().GetResult();
        FlushPendingSettingsSaveAsync().GetAwaiter().GetResult();
        _isDisposed = true;
        _pollCancellation?.Cancel();
        foreach (DeviceRowViewModel device in _allDeviceRows)
            device.PropertyChanged -= OnDeviceRowPropertyChanged;

        DeviceInfo.PropertyChanged -= OnDeviceInfoPropertyChanged;
        _deviceActionCoordinatorService.OperationStateChanged -= OnDeviceActionStateChanged;
        _deviceProcessStateService.ProcessChanged -= OnDeviceProcessChanged;

        _pollCancellation?.Dispose();
        _pollCancellation = null;
        _actionLifetimeCancellation.Dispose();
    }

    private sealed record DeviceRowEditSnapshot(
        string Serial,
        string Name,
        string Type,
        CarrierCountryOption? Country,
        CarrierOption? Carrier,
        bool IncludeSelectedCarrierConfig);

    private sealed class PendingDeviceEdit
    {
        public PendingDeviceEdit(
            DeviceRowEditSnapshot snapshot,
            CancellationTokenSource cancellation)
        {
            Snapshot = snapshot;
            Cancellation = cancellation;
        }

        public DeviceRowEditSnapshot Snapshot { get; }
        public CancellationTokenSource Cancellation { get; }
        public Task PersistenceTask { get; set; } = Task.CompletedTask;
    }

    private sealed class BatchActionTarget
    {
        private readonly CancellationTokenSource _invalidation = new();
        private int _state;
        private int _completionOwner;
        private int _isDisposed;

        public BatchActionTarget(
            DeviceRowViewModel device,
            IDeviceActionOperation operation,
            DeviceInfoApiDevice? deviceProfile,
            SimProfile? simProfile,
            DeviceChangeOptions? changeOptions = null,
            bool changeSimEnabled = false,
            CarrierCountryOption? country = null,
            CarrierOption? carrier = null,
            RandomDeviceRequest? randomDeviceRequest = null,
            IReadOnlyList<StoredDeviceConfig>? deviceConfigurationSnapshot = null)
        {
            Device = device;
            Serial = device.Serial;
            Operation = operation ?? throw new ArgumentNullException(nameof(operation));
            DeviceProfile = deviceProfile;
            SimProfile = simProfile;
            ChangeOptions = changeOptions;
            ChangeSimEnabled = changeSimEnabled;
            Country = country;
            Carrier = carrier;
            RandomDeviceRequest = randomDeviceRequest;
            DeviceConfigurationSnapshot = deviceConfigurationSnapshot == null
                ? null
                : deviceConfigurationSnapshot
                    .Select(CloneStoredDeviceConfig)
                    .ToList();
        }

        public DeviceRowViewModel Device { get; }
        public string Serial { get; }
        public IDeviceActionOperation Operation { get; }
        public CancellationToken OperationToken => Operation.CancellationToken;
        public DeviceInfoApiDevice? DeviceProfile { get; }
        public SimProfile? SimProfile { get; }
        public DeviceChangeOptions? ChangeOptions { get; }
        public bool ChangeSimEnabled { get; }
        public CarrierCountryOption? Country { get; }
        public CarrierOption? Carrier { get; }
        public RandomDeviceRequest? RandomDeviceRequest { get; }
        public List<StoredDeviceConfig>? DeviceConfigurationSnapshot { get; }
        public CancellationToken InvalidationToken => _invalidation.Token;
        public bool IsInvalidated => Volatile.Read(ref _state) == 2;
        public bool IsCompletionOwnedByWorker => Volatile.Read(ref _completionOwner) == 1;

        public void TransferCompletionOwnershipToWorker()
        {
            Interlocked.Exchange(ref _completionOwner, 1);
        }

        public bool TryStartExecution()
        {
            return Interlocked.CompareExchange(ref _state, 1, 0) == 0;
        }

        public bool TryInvalidateQueued()
        {
            return Interlocked.CompareExchange(ref _state, 2, 0) == 0;
        }

        public void CancelQueuedExecution()
        {
            _invalidation.Cancel();
            Operation.Dispose();
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _isDisposed, 1) != 0)
                return;

            _invalidation.Cancel();
            Operation.Dispose();
            _invalidation.Dispose();
        }
    }
}
