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
    private readonly IDeviceActionEligibilityService _deviceActionEligibilityService;
    private readonly IDeviceActionFeedbackService _deviceActionFeedbackService;
    private readonly IClipboardService _clipboardService;
    private readonly IDeviceTimezoneService _deviceTimezoneService;
    private readonly IChangeLocationDialogService _changeLocationDialogService;
    private readonly IChangeTimezoneDialogService _changeTimezoneDialogService;
    private readonly IInstallPackageDialogService _installPackageDialogService;
    private readonly IPackageInstallService _packageInstallService;
    private readonly ILocalizationService _localizationService;
    private readonly IMultipleDeviceConfigService _multipleDeviceConfigService;
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
    private readonly ObservableCollection<DeviceRowViewModel> _selectedDevices = [];
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
    private bool _isShowingToolbarLog;
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
        IPackageInstallService packageInstallService,
        IDeviceActionEligibilityService deviceActionEligibilityService,
        IDeviceActionFeedbackService deviceActionFeedbackService,
        IClipboardService clipboardService)
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
        _deviceActionEligibilityService = deviceActionEligibilityService;
        _deviceActionFeedbackService = deviceActionFeedbackService;
        _clipboardService = clipboardService;
        _deviceTimezoneService = deviceTimezoneService;
        _changeLocationDialogService = changeLocationDialogService;
        _changeTimezoneDialogService = changeTimezoneDialogService;
        _installPackageDialogService = installPackageDialogService;
        _packageInstallService = packageInstallService;
        _localizationService = localizationService;
        _multipleDeviceConfigService = multipleDeviceConfigService;
        _randomDeviceService = randomDeviceService;
        _simProfileService = simProfileService;
        _settingsService = settingsService;
        _uiDispatcher = uiDispatcher;
        _pollingService = pollingService;
        _settings = settings;
        _logger = logger;

        Devices = [];
        SelectedDevices = new ReadOnlyObservableCollection<DeviceRowViewModel>(_selectedDevices);
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
        _deviceActionCoordinatorService.OperationStateChanged += OnDeviceActionStateChanged;
        _deviceProcessStateService.ProcessChanged += OnDeviceProcessChanged;
        RefreshRunningActions();
    }

    public ObservableCollection<DeviceRowViewModel> Devices { get; }
    public ReadOnlyObservableCollection<DeviceRowViewModel> SelectedDevices { get; }
    public ObservableCollection<RunningActionItemViewModel> RunningActions { get; } = [];
    public ObservableCollection<CarrierCountryOption> Countries { get; }
    public ObservableCollection<CarrierOption> Carriers { get; }
    public ObservableCollection<string> AndroidVersions { get; }
    public IReadOnlyList<string> Brands { get; }
    public IReadOnlyList<string> TypeOptions { get; }
    public IReadOnlyDictionary<string, double> DeviceTableColumnRatios =>
        _settings.DeviceTableColumnRatios;

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
        if (!_isApplyingConfiguration)
            QueueConfigurationSave();
    }

    [RelayCommand(CanExecute = nameof(CanAddNewDevices))]
    private async Task AddNewDevicesAsync(CancellationToken cancellationToken)
    {
        IsLoadingDevices = true;
        await ShowToolbarLogAsync("Log_AddDevicesOpening", cancellationToken).ConfigureAwait(true);
        try
        {
            IReadOnlyList<StoredDeviceConfig> selectedDevices =
                await _addDevicesDialogService
                    .ShowAddDevicesAsync(cancellationToken)
                    .ConfigureAwait(true);
            if (selectedDevices.Count == 0)
            {
                await ShowToolbarLogAsync("Log_ActionCanceled", cancellationToken).ConfigureAwait(true);
                return;
            }

            await ShowToolbarLogAsync("Log_SavingDevices", cancellationToken).ConfigureAwait(true);

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

            await ShowToolbarLogAsync("Log_AddDevicesSuccess", cancellationToken).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Failed to add devices from Multiple Device screen.");
            await ShowToolbarLogAsync("Log_AddDevicesFailed", CancellationToken.None).ConfigureAwait(true);
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

    private async Task ShowToolbarLogAsync(
        string resourceKey,
        CancellationToken cancellationToken)
    {
        _isShowingToolbarLog = true;
        NewDeviceCountText = GetLogText(resourceKey);
        _logger.LogInformation("Devices toolbar action: {Message}", NewDeviceCountText);
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

    [RelayCommand]
    private void ToggleMultipleDevicesSelection(DeviceRowViewModel? device)
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
        return SelectedDevices.Count > 0;
    }

    partial void OnIsLoadingDevicesChanged(bool value)
    {
        AddNewDevicesCommand.NotifyCanExecuteChanged();
    }

    private DeviceRowViewModel[] GetSelectedDevicesSnapshot()
    {
        return SelectedDevices.ToArray();
    }

    private async Task<HashSet<DeviceRowViewModel>> CheckInitialTargetEligibilityAsync(
        IReadOnlyList<DeviceRowViewModel> devices,
        CancellationToken cancellationToken)
    {
        Task<(DeviceRowViewModel Device, DeviceActionEligibilityFailure Failure)>[] checks = devices
            .Select(async device =>
            {
                DeviceActionEligibilityFailure failure = await _deviceActionEligibilityService
                    .CheckAsync(
                        device.Serial,
                        DeviceActionRequirement.Online | DeviceActionRequirement.Idle,
                        cancellationToken)
                    .ConfigureAwait(false);
                return (device, failure);
            })
            .ToArray();
        (DeviceRowViewModel Device, DeviceActionEligibilityFailure Failure)[] results = await Task
            .WhenAll(checks)
            .ConfigureAwait(true);

        var eligible = new HashSet<DeviceRowViewModel>();
        foreach ((DeviceRowViewModel device, DeviceActionEligibilityFailure failure) in results)
        {
            ApplyLiveConnectionState(device, failure);
            if (failure != DeviceActionEligibilityFailure.None)
            {
                _deviceActionFeedbackService.ReportEligibilityFailure(device.Serial, failure);
                continue;
            }

            eligible.Add(device);
        }

        return eligible;
    }

    [RelayCommand(CanExecute = nameof(CanRunSelectedDeviceBatchAction), AllowConcurrentExecutions = true)]
    private Task RandomizeMultipleDevicesInfoAsync()
    {
        return StartTrackedBatchWorkflow(RunRandomSelectedDevicesAsync);
    }

    private async Task RunRandomSelectedDevicesAsync(CancellationToken batchToken)
    {
        var targets = new List<BatchActionTarget>();
        Guid sessionId = Guid.NewGuid();
        try
        {
            DeviceRowViewModel[] selectedDevices = GetSelectedDevicesSnapshot();
            if (selectedDevices.Length == 0)
                return;

            HashSet<DeviceRowViewModel> eligibleDevices = await CheckInitialTargetEligibilityAsync(
                    selectedDevices,
                    batchToken)
                .ConfigureAwait(true);
            if (eligibleDevices.Count == 0)
                return;

            MultipleDeviceConfiguration? actionConfiguration =
                await LoadActionConfigurationSnapshotAsync(eligibleDevices, batchToken)
                    .ConfigureAwait(true);
            if (actionConfiguration == null)
                return;

            RandomDeviceRequest request = CreateRandomDeviceRequest(actionConfiguration);
            foreach (DeviceRowViewModel device in selectedDevices)
            {
                if (!eligibleDevices.Contains(device))
                    continue;

                IDeviceActionOperation? operation = TryStartBatchAction(
                    device,
                    DeviceActionKind.RandomDevice,
                    batchToken,
                    sessionId);
                if (operation != null)
                {
                    var target = new BatchActionTarget(
                        device,
                        operation,
                        deviceProfile: null,
                        simProfile: null,
                        randomDeviceRequest: CreateRandomDeviceRequestCopy(request));
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
            await SetBatchCancellationResultsAsync(targets)
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
    private Task ChangeAndWipeMultipleDevicesAsync()
    {
        return StartTrackedBatchWorkflow(
            workflowCancellation => RunSelectedDeviceBatchActionAsync(
                DeviceActionKind.ChangeDevice,
                workflowCancellation));
    }

    [RelayCommand(CanExecute = nameof(CanRunSelectedDeviceBatchAction), AllowConcurrentExecutions = true)]
    private Task RandomizeChangeAndWipeMultipleDevicesAsync()
    {
        return StartTrackedBatchWorkflow(RunRandomChangeAndWipeSelectedDevicesAsync);
    }

    [RelayCommand(CanExecute = nameof(CanRunSelectedDeviceBatchAction), AllowConcurrentExecutions = true)]
    private Task ChangeMultipleDevicesWithoutWipeAsync()
    {
        return StartTrackedBatchWorkflow(
            workflowCancellation => RunSelectedDeviceBatchActionAsync(
                DeviceActionKind.ChangeWithoutWipe,
                workflowCancellation));
    }

    [RelayCommand(CanExecute = nameof(CanRunSelectedDeviceBatchAction), AllowConcurrentExecutions = true)]
    private Task WipeMultipleDevicesWithoutChangeAsync()
    {
        return StartTrackedBatchWorkflow(
            workflowCancellation => RunSelectedDeviceBatchActionAsync(
                DeviceActionKind.Wipe,
                workflowCancellation));
    }

    [RelayCommand(CanExecute = nameof(CanRunSelectedDeviceBatchAction), AllowConcurrentExecutions = true)]
    private Task RandomizeMultipleDevicesSimInfoAsync()
    {
        return StartTrackedBatchWorkflow(
            workflowCancellation => RunSelectedDeviceBatchActionAsync(
                DeviceActionKind.RandomSim,
                workflowCancellation));
    }

    [RelayCommand(CanExecute = nameof(CanRunSelectedDeviceBatchAction), AllowConcurrentExecutions = true)]
    private Task ChangeMultipleDevicesSimInfoAsync()
    {
        return StartTrackedBatchWorkflow(
            workflowCancellation => RunSelectedDeviceBatchActionAsync(
                DeviceActionKind.ChangeSim,
                workflowCancellation));
    }

    [RelayCommand(CanExecute = nameof(CanRunSelectedDeviceBatchAction), AllowConcurrentExecutions = true)]
    private Task ChangeMultipleDevicesLocationAsync()
    {
        return StartLocationTimezoneWorkflowAsync(isLocation: true);
    }

    [RelayCommand(CanExecute = nameof(CanRunSelectedDeviceBatchAction), AllowConcurrentExecutions = true)]
    private Task ChangeMultipleDevicesTimezoneAsync()
    {
        return StartLocationTimezoneWorkflowAsync(isLocation: false);
    }

    [RelayCommand(CanExecute = nameof(CanRunSelectedDeviceBatchAction), AllowConcurrentExecutions = true)]
    private Task InstallPackagesOnMultipleDevicesAsync()
    {
        DeviceRowViewModel[] selectedDevices = GetSelectedDevicesSnapshot();
        if (selectedDevices.Length == 0)
            return Task.CompletedTask;

        return StartTrackedBatchWorkflow(
            workflowCancellation => RunSelectedInstallPackageWorkflowAsync(
                selectedDevices,
                workflowCancellation));
    }

    private Task StartLocationTimezoneWorkflowAsync(bool isLocation)
    {
        return StartTrackedBatchWorkflow(
            cancellationToken => RunSelectedLocationOrTimezoneAsync(isLocation, cancellationToken));
    }

    private async Task RunSelectedLocationOrTimezoneAsync(
        bool isLocation,
        CancellationToken cancellationToken)
    {
        var targets = new List<BatchActionTarget>();
        Guid sessionId = Guid.NewGuid();
        try
        {
            DeviceRowViewModel[] selectedDevices = GetSelectedDevicesSnapshot();
            if (selectedDevices.Length == 0)
                return;

            DeviceActionKind actionKind = isLocation
                ? DeviceActionKind.ChangeLocation
                : DeviceActionKind.ChangeTimezone;
            targets = await CreateReservedEligibleTargetsAsync(
                    selectedDevices,
                    cancellationToken,
                    actionKind,
                    sessionId)
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
                await SetBatchDialogDismissalResultsAsync(targets)
                    .ConfigureAwait(true);
                return;
            }


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
            await SetBatchCancellationResultsAsync(targets)
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
            targets = await CreateReservedEligibleTargetsAsync(
                    selectedDevices,
                    cancellationToken,
                    DeviceActionKind.InstallPackages,
                    sessionId)
                .ConfigureAwait(true);
            if (targets.Count == 0)
                return;

            InstallPackageBatchRequest? request = await _installPackageDialogService
                .ShowInstallPackageBatchAsync(targets.Count, cancellationToken)
                .ConfigureAwait(true);
            if (request == null)
            {
                await SetBatchDialogDismissalResultsAsync(targets)
                    .ConfigureAwait(true);

                return;
            }


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
            await SetBatchCancellationResultsAsync(targets)
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

                if (!await CanStartBatchTargetAsync(target, targetCancellation.Token)
                        .ConfigureAwait(false))
                {
                    return;
                }

                if (!IsCurrentTarget(target))
                    return;

                await RunOnUiContextAsync(() => SetTargetLog(
                        target,
                        "Log_InstallPackageInstalling"))
                    .ConfigureAwait(false);
                InstallPackageSetResult result = await _packageInstallService
                    .InstallManyAsync(
                        target.Serial,
                        request.FilePaths,
                        request.Options,
                        targetCancellation.Token)
                    .ConfigureAwait(false);
                await RunOnUiContextAsync(() => SetTargetLog(
                        target,
                        result.MessageResourceKey,
                        result.MessageArguments.ToArray()))
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
            await SetTargetCancellationResultAsync(target)
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
        Func<CancellationToken, Task> workflow)
    {
        var workflowCancellation = CancellationTokenSource.CreateLinkedTokenSource(
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
        DeviceActionKind logicalActionKind,
        Guid sessionId)
    {
        async Task<(DeviceRowViewModel Device, DeviceActionEligibilityFailure Failure)> CheckAsync(
            DeviceRowViewModel device)
        {
            DeviceActionEligibilityFailure failure = await _deviceActionEligibilityService
                .CheckAsync(
                    device.Serial,
                    DeviceActionRequirement.Online | DeviceActionRequirement.Idle,
                    cancellationToken)
                .ConfigureAwait(false);
            return (device, failure);
        }

        (DeviceRowViewModel Device, DeviceActionEligibilityFailure Failure)[] checks = await Task
            .WhenAll(selectedDevices.Select(CheckAsync))
            .ConfigureAwait(true);
        var targets = new List<BatchActionTarget>(checks.Length);
        foreach ((DeviceRowViewModel device, DeviceActionEligibilityFailure failure) in checks)
        {
            if (failure != DeviceActionEligibilityFailure.None)
            {
                await RunOnUiContextAsync(() =>
                {
                    ApplyLiveConnectionState(device, failure);
                    _deviceActionFeedbackService.ReportEligibilityFailure(device.Serial, failure);
                })
                    .ConfigureAwait(true);
                continue;
            }

            await RunOnUiContextAsync(() => ApplyLiveConnectionState(device, failure))
                .ConfigureAwait(true);

            IDeviceActionOperation? operation = TryStartBatchAction(
                device,
                logicalActionKind,
                cancellationToken,
                sessionId);
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
                simProfile: null);
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

                if (!await CanStartBatchTargetAsync(target, targetCancellation.Token)
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
                        targetCancellation.Token)
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
            await SetTargetCancellationResultAsync(target)
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

                if (!await CanStartBatchTargetAsync(target, targetCancellation.Token)
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
                        targetCancellation.Token)
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
            await SetTargetCancellationResultAsync(target)
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

    private async Task<bool> PersistLocationConfigAsync(
        string serial,
        ChangeLocationMode mode,
        DeviceLocationResult result,
        CancellationToken cancellationToken)
    {
        await _deviceRefreshLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await _deviceConfigService.SaveLocationConfigAsync(
                    _storedDevices,
                    serial,
                    mode,
                    result.Latitude,
                    result.Longitude,
                    result.CountryCode,
                    result.CityName,
                    cancellationToken)
                .ConfigureAwait(false);
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
        CancellationToken cancellationToken)
    {
        await _deviceRefreshLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await _deviceConfigService.SaveTimezoneConfigAsync(
                    _storedDevices,
                    serial,
                    mode,
                    timezone,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            _deviceRefreshLock.Release();
        }
    }

    [RelayCommand]
    private async Task CopySerialAsync(DeviceRowViewModel? device, CancellationToken cancellationToken)
    {
        if (device == null || string.IsNullOrWhiteSpace(device.Serial))
            return;

        try
        {
            await RunOnUiContextAsync(() => _clipboardService.SetText(device.Serial))
                .ConfigureAwait(true);
            SetContextDeviceLog(device, "Log_CopySerialSuccess");
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Failed to copy serial for device {Serial}.", device.Serial);
            SetContextDeviceLog(device, "Log_CopySerialFailed");
        }
    }

    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task RefreshContextMenuStateAsync(DeviceRowViewModel? device)
    {
        try
        {
            device = await GetContextOnlineDeviceAsync(
                    device,
                    logOffline: false)
                .ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            return;
        }
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

            SetContextDeviceLog(device, (isGms, enabled) switch
            {
                (true, true) => "Log_GmsEnabled",
                (true, false) => "Log_GmsDisabled",
                (false, true) => "Log_PlayStoreEnabled",
                _ => "Log_PlayStoreDisabled"
            });
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
            _logger.LogError(exception, "Failed to toggle package state for {Serial}.", device.Serial);
            SetContextDeviceLog(
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
            _logger.LogError(exception, "Failed to toggle Wi-Fi for {Serial}.", device.Serial);
            SetContextDeviceLog(device, "Log_WifiToggleFailed");
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
            SetContextDeviceLog(device, "Log_RebootingDevice");
            await _deviceActionService
                .RebootAsync(device.Serial, CancellationToken.None)
                .ConfigureAwait(true);
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

    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task DeleteDeviceAsync(DeviceRowViewModel? device)
    {
        if (device == null)
            return;

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

        using IDeviceActionOperation? operation = TryStartContextAction(device);
        if (operation == null)
        {
            ShowBusyActionLog(device);
            return;
        }

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
                await SetContextDialogDismissalLogAsync(device, operation);
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

            await ShowToolbarLogAsync("Log_DeleteDeviceSuccess", cancellationToken)
                .ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            await SetContextOperationCancellationLogAsync(device, operation);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Failed to delete device {Serial}.", device.Serial);
            SetDeviceLog(device, "Log_DeleteDeviceFailed");
        }
    }

    private async Task<DeviceRowViewModel?> GetContextOnlineDeviceAsync(
        DeviceRowViewModel? device,
        bool logOffline = true)
    {
        if (device == null)
            return null;

        DeviceActionEligibilityFailure failure = await _deviceActionEligibilityService
            .CheckAsync(
                device.Serial,
                DeviceActionRequirement.Online,
                CancellationToken.None)
            .ConfigureAwait(true);
        ApplyLiveConnectionState(device, failure);
        if (failure == DeviceActionEligibilityFailure.None)
            return device;

        if (logOffline)
            _deviceActionFeedbackService.ReportEligibilityFailure(device.Serial, failure);

        return null;
    }

    private IDeviceActionOperation? TryStartContextAction(DeviceRowViewModel device)
    {
        IDeviceActionOperation? operation = _deviceActionCoordinatorService.TryStart(
            device.Serial,
            DeviceActionKind.DeleteDevice,
            canCancel: false,
            externalCancellationToken: _actionLifetimeCancellation.Token,
            source: DeviceActionSource.MultipleDevices);
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
        DeviceActionKind action,
        CancellationToken cancellationToken)
    {
        var targets = new List<BatchActionTarget>();
        Guid sessionId = Guid.NewGuid();
        try
        {
            DeviceRowViewModel[] selectedDevices = GetSelectedDevicesSnapshot();
            if (selectedDevices.Length == 0)
                return;

            HashSet<DeviceRowViewModel> eligibleDevices = await CheckInitialTargetEligibilityAsync(
                    selectedDevices,
                    cancellationToken)
                .ConfigureAwait(true);
            if (eligibleDevices.Count == 0)
                return;

            MultipleDeviceConfiguration? actionConfiguration =
                await LoadActionConfigurationSnapshotAsync(eligibleDevices, cancellationToken)
                    .ConfigureAwait(true);
            if (actionConfiguration == null)
                return;

            DeviceChangeOptions changeOptions = DeviceChangeOptionsHelper.CreateNormalizedCopy(
                actionConfiguration.ChangeOptions);
            bool changeSimEnabled = actionConfiguration.ChangeConfig.ChangeSimEnabled;
            CarrierCountryOption? country = CreateCountryOption(actionConfiguration.ChangeConfig);
            CarrierOption? carrier = CreateCarrierOption(actionConfiguration.ChangeConfig);

            foreach (DeviceRowViewModel device in selectedDevices)
            {
                if (!eligibleDevices.Contains(device))
                    continue;

                DeviceInfoApiDevice? deviceProfile = null;
                if (action is DeviceActionKind.ChangeDevice or DeviceActionKind.ChangeWithoutWipe)
                {
                    if (!_randomDeviceProfiles.TryGetValue(device.Serial, out deviceProfile))
                    {
                        SetDeviceLog(device, "Log_RandomDeviceRequired");
                        continue;
                    }
                }
                SimProfile? simProfile = null;
                if (action == DeviceActionKind.ChangeSim
                    && !_randomSimProfiles.TryGetValue(device.Serial, out simProfile))
                {
                    SetDeviceLog(device, "Log_RandomSimRequired");
                    continue;
                }

                IDeviceActionOperation? operation = TryStartBatchAction(
                    device,
                    action,
                    cancellationToken,
                    sessionId);
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
                    country,
                    carrier);
                RegisterBatchTarget(target);
                targets.Add(target);
            }

            if (targets.Count == 0)
                return;

            if (action != DeviceActionKind.RandomSim)
            {
                bool confirmed = await _deviceActionConfirmationDialogService
                    .ConfirmMultipleAsync(action, targets.Count, cancellationToken)
                    .ConfigureAwait(true);
                if (!confirmed)
                {
                    await SetBatchDialogDismissalResultsAsync(targets)
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
            await SetBatchCancellationResultsAsync(targets)
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
            DeviceRowViewModel[] selectedDevices = GetSelectedDevicesSnapshot();
            if (selectedDevices.Length == 0)
                return;

            HashSet<DeviceRowViewModel> eligibleDevices = await CheckInitialTargetEligibilityAsync(
                    selectedDevices,
                    cancellationToken)
                .ConfigureAwait(true);
            if (eligibleDevices.Count == 0)
                return;

            MultipleDeviceConfiguration? actionConfiguration =
                await LoadActionConfigurationSnapshotAsync(eligibleDevices, cancellationToken)
                    .ConfigureAwait(true);
            if (actionConfiguration == null)
                return;

            DeviceChangeOptions changeOptions = DeviceChangeOptionsHelper.CreateNormalizedCopy(
                actionConfiguration.ChangeOptions);
            bool changeSimEnabled = actionConfiguration.ChangeConfig.ChangeSimEnabled;
            RandomDeviceRequest randomRequest = CreateRandomDeviceRequest(actionConfiguration);

            foreach (DeviceRowViewModel device in selectedDevices)
            {
                if (!eligibleDevices.Contains(device))
                    continue;

                IDeviceActionOperation? operation = TryStartBatchAction(
                    device,
                    DeviceActionKind.RandomChangeAndWipe,
                    cancellationToken,
                    sessionId);
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
                    randomDeviceRequest: CreateRandomDeviceRequestCopy(randomRequest));
                RegisterBatchTarget(target);
                targets.Add(target);
            }

            if (targets.Count == 0)
                return;

            bool confirmed = await _deviceActionConfirmationDialogService
                .ConfirmMultipleAsync(DeviceActionKind.RandomChangeAndWipe, targets.Count, cancellationToken)
                .ConfigureAwait(true);
            if (!confirmed)
            {
                await SetBatchDialogDismissalResultsAsync(targets)
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
            await SetBatchCancellationResultsAsync(targets)
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
        DeviceActionKind action,
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
                    case DeviceActionKind.ChangeDevice:
                        await _deviceChangeService.ChangeAsync(
                                target.Serial,
                                target.DeviceProfile!,
                                target.ChangeSimEnabled,
                                target.ChangeOptions!,
                                progress,
                                targetCancellation.Token)
                            .ConfigureAwait(false);
                        break;
                    case DeviceActionKind.ChangeWithoutWipe:
                        await _deviceChangeService.ChangeWithoutWipeAsync(
                                target.Serial,
                                target.DeviceProfile!,
                                target.ChangeSimEnabled,
                                target.ChangeOptions!,
                                progress,
                                targetCancellation.Token)
                            .ConfigureAwait(false);
                        break;
                    case DeviceActionKind.Wipe:
                        await _deviceChangeService.WipeWithoutChangeAsync(
                                target.Serial,
                                target.ChangeOptions!,
                                progress,
                                targetCancellation.Token)
                            .ConfigureAwait(false);
                        break;
                    case DeviceActionKind.ChangeSim:
                    {
                        SimProfile editedProfile = target.SimProfile!;
                        await _deviceChangeService.ChangeSimAsync(
                                target.Serial,
                                editedProfile,
                                targetCancellation.Token)
                            .ConfigureAwait(false);
                        await RunOnUiContextAsync(() => ApplyRandomSimInfo(target, editedProfile))
                            .ConfigureAwait(false);
                        break;
                    }
                    case DeviceActionKind.RandomSim:
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
            await SetTargetCancellationResultAsync(target)
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
                    await RunOnUiContextAsync(() => SetTargetLog(target, "Log_RandomAndChangeDeviceFailed"))
                        .ConfigureAwait(false);
                    return;
                }

                DeviceInfoApiDevice profile = CloneDeviceProfile(randomResult.Profile)!;
                await RunOnUiContextAsync(() => ApplyRandomDeviceInfo(target, profile.Clone()))
                    .ConfigureAwait(false);

                if (!await CanStartBatchTargetAsync(target, targetCancellation.Token).ConfigureAwait(false))
                    return;

                await _deviceChangeService.ChangeAsync(
                        target.Serial,
                        profile,
                        target.ChangeSimEnabled,
                        target.ChangeOptions!,
                        CreateDeviceChangeProgress(target, DeviceActionKind.RandomChangeAndWipe),
                        targetCancellation.Token)
                    .ConfigureAwait(false);
                await RunOnUiContextAsync(() => SetTargetLog(target, "Log_RandomAndChangeDeviceSuccess"))
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
            await SetTargetCancellationResultAsync(target)
                .ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception,
                "Failed to randomize, change, and wipe device {Serial}.", target.Serial);
            await RunOnUiContextAsync(() => SetTargetLog(target, "Log_RandomAndChangeDeviceFailed"))
                .ConfigureAwait(false);
        }
        finally
        {
            CompleteBatchTarget(target);
        }
    }

    private async Task<MultipleDeviceConfiguration?> LoadActionConfigurationSnapshotAsync(
        IReadOnlyCollection<DeviceRowViewModel> targetDevices,
        CancellationToken cancellationToken)
    {
        MultipleDeviceConfiguration configuration = CreateConfiguration();
        bool saved = await FlushPendingConfigurationSaveAsync(
                configuration,
                cancellationToken)
            .ConfigureAwait(false);
        if (!saved)
        {
            await RunOnUiContextAsync(() =>
            {
                foreach (DeviceRowViewModel device in targetDevices)
                {
                    _deviceActionFeedbackService.SetNonOwningProcess(
                        device.Serial,
                        "Log_ActionConfigurationSaveFailed");
                }
            }).ConfigureAwait(false);
            return null;
        }

        await RefreshStoredDevicesFromDiskAsync(cancellationToken).ConfigureAwait(false);
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
                    .Select(StoredDeviceConfigSnapshot.Create)
                    .ToList();
            }).ConfigureAwait(false);
        }
        finally
        {
            _deviceRefreshLock.Release();
        }
    }

    private IProgress<DeviceChangeStage> CreateDeviceChangeProgress(
        BatchActionTarget target,
        DeviceActionKind action)
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

    private static string GetStartLogKey(DeviceActionKind action)
    {
        return action switch
        {
            DeviceActionKind.ChangeDevice => "Log_ChangeDevice",
            DeviceActionKind.ChangeWithoutWipe => "Log_ChangeWithoutWipe",
            DeviceActionKind.Wipe => "Log_WipeWithoutChange",
            DeviceActionKind.ChangeSim => "Log_ChangeSim",
            DeviceActionKind.RandomSim => "Log_RandomSim",
            DeviceActionKind.RandomChangeAndWipe => "Log_RandomAndChangeDevice",
            _ => throw new ArgumentOutOfRangeException(nameof(action), action, null)
        };
    }

    private static string GetSuccessLogKey(DeviceActionKind action)
    {
        return action switch
        {
            DeviceActionKind.ChangeDevice => "Log_ChangeDeviceSuccess",
            DeviceActionKind.ChangeWithoutWipe => "Log_ChangeWithoutWipeSuccess",
            DeviceActionKind.Wipe => "Log_WipeWithoutChangeSuccess",
            DeviceActionKind.ChangeSim => "Log_ChangeSimSuccess",
            DeviceActionKind.RandomSim => "Log_RandomSimSuccess",
            DeviceActionKind.RandomChangeAndWipe => "Log_RandomAndChangeDeviceSuccess",
            _ => throw new ArgumentOutOfRangeException(nameof(action), action, null)
        };
    }

    private static string GetFailureLogKey(DeviceActionKind action)
    {
        return action switch
        {
            DeviceActionKind.ChangeDevice => "Log_ChangeDeviceFailed",
            DeviceActionKind.ChangeWithoutWipe => "Log_ChangeWithoutWipeFailed",
            DeviceActionKind.Wipe => "Log_WipeWithoutChangeFailed",
            DeviceActionKind.ChangeSim => "Log_ChangeSimFailed",
            DeviceActionKind.RandomSim => "Log_RandomSimFailed",
            DeviceActionKind.RandomChangeAndWipe => "Log_RandomAndChangeDeviceFailed",
            _ => throw new ArgumentOutOfRangeException(nameof(action), action, null)
        };
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
            await SetTargetCancellationResultAsync(target)
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
        if (!IsCurrentTarget(target))
            return false;

        DeviceActionEligibilityFailure failure = await _deviceActionEligibilityService
            .CheckAsync(
                target.Serial,
                DeviceActionRequirement.Online,
                cancellationToken)
            .ConfigureAwait(false);
        if (failure == DeviceActionEligibilityFailure.None)
        {
            await RunOnUiContextAsync(() =>
                    ApplyLiveConnectionState(target.Device, failure))
                .ConfigureAwait(false);
            return true;
        }

        await RunOnUiContextAsync(() =>
        {
            ApplyLiveConnectionState(target.Device, failure);
            _deviceActionFeedbackService.ReportEligibilityFailure(target.Serial, failure);
        })
            .ConfigureAwait(false);
        return false;
    }

    [RelayCommand]
    private async Task SaveMultipleDevicesColumnRatiosAsync(
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
    private async Task OpenMultipleDevicesAdvancedConfigAsync(CancellationToken cancellationToken)
    {
        string[] deviceSerials = GetSelectedDevicesSnapshot()
            .Select(device => device.Serial)
            .ToArray();
        if (deviceSerials.Length == 0)
            return;

        if (UseDefaultChangeMode)
        {
            foreach (string serial in deviceSerials)
            {
                _deviceActionFeedbackService.SetNonOwningProcess(
                    serial,
                    "Log_AdvancedConfigDefaultModeRequired");
            }
            return;
        }

        try
        {
            foreach (string serial in deviceSerials)
                _deviceActionFeedbackService.SetNonOwningProcess(serial, "Log_OpeningDialog");

            DeviceChangeOptions optionsSnapshot =
                DeviceChangeOptionsHelper.CreateNormalizedCopy(_changeOptions);
            AdvancedChangeConfigDialogResult? result = deviceSerials.Length == 1
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
            {
                foreach (string serial in deviceSerials)
                    _deviceActionFeedbackService.ReportNonOwningDialogDismissed(serial);
                return;
            }

            _isApplyingConfiguration = true;
            try
            {
                _changeOptions = DeviceChangeOptionsHelper.CreateNormalizedCopy(result.Options);
                UseIntegritySecurityPatch = result.UseIntegritySecurityPatch;
            }
            finally
            {
                _isApplyingConfiguration = false;
            }

            MultipleDeviceConfiguration configurationSnapshot = CreateConfiguration();
            bool saved = await FlushPendingConfigurationSaveAsync(
                    configurationSnapshot,
                    cancellationToken)
                .ConfigureAwait(true);
            if (!saved)
            {
                foreach (string serial in deviceSerials)
                {
                    _deviceActionFeedbackService.SetNonOwningProcess(
                        serial,
                        "Log_AdvancedChangeConfigFailed");
                }
                return;
            }

            foreach (string serial in deviceSerials)
                _deviceActionFeedbackService.SetNonOwningProcess(serial, "Log_AdvancedChangeConfigSaved");
        }
        catch (OperationCanceledException)
        {
            foreach (string serial in deviceSerials)
            {
                await _deviceActionFeedbackService.ReportOperationCanceledAsync(
                        serial,
                        DeviceActionCancellationReason.External,
                        requiresOnline: false,
                        CancellationToken.None)
                    .ConfigureAwait(true);
            }
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Failed to open Advanced Change Config for Multiple Device preset.");
            foreach (string serial in deviceSerials)
                _deviceActionFeedbackService.SetNonOwningProcess(serial, "Log_AdvancedChangeConfigFailed");
        }
    }

    private bool CanOpenAdvancedChangeConfig()
    {
        return SelectedDevices.Count > 0;
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

        var selectedSerials = _allDeviceRows.Count > 0
            ? SelectedDevices
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
        return DeviceRowFilterHelper.Matches(device, SelectedDeviceFilter, DeviceSearchText);
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

            NotifyActionPresentationChanged();
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
                        session.Source,
                        GetLogText(session.Kind.GetDisplayResourceKey()),
                        GetLogText(session.Source.GetDisplayResourceKey()),
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

    private bool IsDeviceBusy(string serial)
    {
        return _deviceActionCoordinatorService.IsBusy(serial);
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

    private string GetLogText(string resourceKey)
    {
        return _localizationService.GetString(resourceKey);
    }

    private void SetDeviceLog(
        DeviceRowViewModel device,
        string resourceKey,
        params object[] formatArguments)
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

    private void ShowBusyActionLog(DeviceRowViewModel device)
    {
        _deviceActionFeedbackService.ReportEligibilityFailure(
            device.Serial,
            DeviceActionEligibilityFailure.Busy);
    }

    private IDeviceActionOperation? TryStartBatchAction(
        DeviceRowViewModel device,
        DeviceActionKind logicalActionKind,
        CancellationToken cancellationToken,
        Guid sessionId)
    {
        return _deviceActionCoordinatorService.TryStart(
            device.Serial,
            logicalActionKind.ToBatchActionKind(),
            canCancel: true,
            externalCancellationToken: cancellationToken,
            sessionId: sessionId,
            source: DeviceActionSource.MultipleDevices);
    }

    private void SetTargetLog(
        BatchActionTarget target,
        string resourceKey,
        params object[] formatArguments)
    {
        if (IsCurrentTarget(target) && IsActiveBatchTarget(target))
            SetDeviceLog(target.Device, resourceKey, formatArguments);
    }

    private Task SetContextOperationCancellationLogAsync(
        DeviceRowViewModel device,
        IDeviceActionOperation operation)
    {
        return _deviceActionFeedbackService.ReportOperationCanceledAsync(
            device.Serial,
            operation.CancellationReason,
            requiresOnline: false,
            CancellationToken.None);
    }

    private async Task SetContextDialogDismissalLogAsync(
        DeviceRowViewModel device,
        IDeviceActionOperation operation)
    {
        if (operation.CancellationReason == DeviceActionCancellationReason.None)
        {
            _deviceActionFeedbackService.ReportDialogDismissed(device.Serial);
            return;
        }

        await SetContextOperationCancellationLogAsync(device, operation);
    }

    private Task SetTargetCancellationResultAsync(BatchActionTarget target)
    {
        return ReportTargetCancellationAsync(target);
    }

    private async Task ReportTargetCancellationAsync(BatchActionTarget target)
    {
        bool shouldReport = false;
        await RunOnUiContextAsync(() =>
        {
            if (target.IsInvalidated)
                return;

            shouldReport = IsCurrentTarget(target) && IsActiveBatchTarget(target);
        }).ConfigureAwait(false);
        if (!shouldReport)
            return;

        await _deviceActionFeedbackService.ReportOperationCanceledAsync(
                target.Serial,
                target.Operation.CancellationReason,
                requiresOnline: true,
                CancellationToken.None)
            .ConfigureAwait(false);
    }

    private async Task SetBatchCancellationResultsAsync(
        IEnumerable<BatchActionTarget> targets)
    {
        foreach (BatchActionTarget target in targets)
        {
            await SetTargetCancellationResultAsync(target)
                .ConfigureAwait(false);
        }
    }

    private Task SetTargetDialogDismissalResultAsync(BatchActionTarget target)
    {
        if (target.Operation.CancellationReason != DeviceActionCancellationReason.None
            || target.IsInvalidated)
        {
            return SetTargetCancellationResultAsync(target);
        }

        return RunOnUiContextAsync(() =>
        {
            if (IsCurrentTarget(target) && IsActiveBatchTarget(target))
                _deviceActionFeedbackService.ReportDialogDismissed(target.Serial);
        });
    }

    private async Task SetBatchDialogDismissalResultsAsync(
        IEnumerable<BatchActionTarget> targets)
    {
        foreach (BatchActionTarget target in targets)
        {
            await SetTargetDialogDismissalResultAsync(target)
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

    private void ApplyRandomDeviceInfo(BatchActionTarget target, DeviceInfoApiDevice randomDevice)
    {
        if (!IsCurrentTarget(target))
            return;

        string serial = target.Serial;
        _randomDeviceProfiles[serial] = randomDevice;
        SimProfile? simProfile = SimProfileHelper.FromDeviceProfile(randomDevice);
        if (simProfile == null)
            _randomSimProfiles.Remove(serial);
        else
            _randomSimProfiles[serial] = simProfile;

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

        for (int index = _selectedDevices.Count - 1; index >= 0; index--)
        {
            if (!selectedRows.Contains(_selectedDevices[index]))
                _selectedDevices.RemoveAt(index);
        }

        for (int index = 0; index < selectedRows.Length; index++)
        {
            DeviceRowViewModel row = selectedRows[index];
            int currentIndex = _selectedDevices.IndexOf(row);
            if (currentIndex < 0)
                _selectedDevices.Insert(index, row);
            else if (currentIndex != index)
                _selectedDevices.Move(currentIndex, index);
        }

    }

    private void NotifySelectionChanged()
    {
        OnPropertyChanged(nameof(AllDevicesSelectionState));
        OpenMultipleDevicesAdvancedConfigCommand.NotifyCanExecuteChanged();
        NotifySelectedBatchActionsCanExecuteChanged();
    }

    private void NotifySelectedBatchActionsCanExecuteChanged()
    {
        RandomizeMultipleDevicesInfoCommand.NotifyCanExecuteChanged();
        ChangeAndWipeMultipleDevicesCommand.NotifyCanExecuteChanged();
        RandomizeChangeAndWipeMultipleDevicesCommand.NotifyCanExecuteChanged();
        ChangeMultipleDevicesWithoutWipeCommand.NotifyCanExecuteChanged();
        WipeMultipleDevicesWithoutChangeCommand.NotifyCanExecuteChanged();
        RandomizeMultipleDevicesSimInfoCommand.NotifyCanExecuteChanged();
        ChangeMultipleDevicesSimInfoCommand.NotifyCanExecuteChanged();
        ChangeMultipleDevicesLocationCommand.NotifyCanExecuteChanged();
        ChangeMultipleDevicesTimezoneCommand.NotifyCanExecuteChanged();
        InstallPackagesOnMultipleDevicesCommand.NotifyCanExecuteChanged();
    }

    private void NotifyActionPresentationChanged()
    {
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

    private async Task RefreshNewDeviceCountAsync(CancellationToken cancellationToken)
    {
        if (_isShowingToolbarLog)
            return;

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

                if (!_isShowingToolbarLog)
                {
                    NewDeviceCountText = FormatCount(
                        "ChangeMultipleDevices_NewDeviceCount",
                        newDeviceCount);
                }
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
                if (!_isShowingToolbarLog)
                {
                    NewDeviceCountText = FormatCount(
                        "ChangeMultipleDevices_NewDeviceCount",
                        _localizationService.GetString("ChangeMultipleDevices_NotAvailable"));
                }
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
            SelectedBrand =
                DeviceProfileOptionsHelper.FindOption(Brands, changeConfig.Brand) ?? "Random";
            UpdateAndroidVersionOptions(SelectedBrand, changeConfig.AndroidVersion);
            SelectedModel = changeConfig.Model;
            IsChangeSimEnabled = changeConfig.ChangeSimEnabled;
            UseIntegritySecurityPatch = changeConfig.UseIntegritySecurityPatch;
            _changeOptions = DeviceChangeOptionsHelper.CreateNormalizedCopy(
                configuration.ChangeOptions);
            UseDefaultChangeMode = _changeOptions.UseDefaultMode;

            CarrierCountryOption? selectedCountry =
                DeviceProfileOptionsHelper.FindCountryByIso(Countries, changeConfig.CountryIso)
                ?? DeviceProfileOptionsHelper.FindCountryByName(Countries, changeConfig.CountryName)
                ?? DeviceProfileOptionsHelper.FindCountryByIso(Countries, DefaultCountryIso)
                ?? Countries.FirstOrDefault();
            SelectedCountry = selectedCountry;
            UpdateCarrierOptionsForCountry(selectedCountry?.CountryIso, changeConfig);
        }
        finally
        {
            _isApplyingConfiguration = false;
        }
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

    private void UpdateAndroidVersionOptions(string? brand, string? preferredVersion)
    {
        AndroidVersions.Clear();
        foreach (string version in DeviceProfileOptionsHelper.GetAndroidVersions(brand))
            AndroidVersions.Add(version);

        SelectedAndroidVersion =
            DeviceProfileOptionsHelper.FindOption(AndroidVersions, preferredVersion) ?? "Random";
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

    private Task<bool> FlushPendingConfigurationSaveAsync()
    {
        return FlushPendingConfigurationSaveAsync(
            CreateConfiguration(),
            CancellationToken.None);
    }

    private async Task<bool> FlushPendingConfigurationSaveAsync(
        MultipleDeviceConfiguration flushSnapshot,
        CancellationToken cancellationToken)
    {
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
        try
        {
            await _multipleDeviceConfigService
                .SaveAsync(flushSnapshot, cancellationToken)
                .ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Failed to flush Multiple Device configuration.");
            return false;
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
            Country = request.Country,
            Carrier = request.Carrier
        };
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
        return DeviceRowFactory.SerialEquals(left, right);
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
                    .Select(StoredDeviceConfigSnapshot.Create)
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
