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
    private readonly IDeviceChangeService _deviceChangeService;
    private readonly IDeviceConfigService _deviceConfigService;
    private readonly IDeviceListService _deviceListService;
    private readonly IDeviceActionGuardService _deviceActionGuardService;
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
    private readonly SemaphoreSlim _batchActionLock = new(1, 1);
    private readonly object _pendingDeviceEditsLock = new();
    private readonly Dictionary<string, PendingDeviceEdit> _pendingDeviceEdits =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly object _pendingConfigSaveLock = new();
    private readonly object _pendingSettingsSaveLock = new();
    private readonly List<DeviceRowViewModel> _allDeviceRows = [];
    private readonly Dictionary<string, DeviceInfoApiDevice> _randomDeviceProfiles =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, SimProfile> _randomSimProfiles =
        new(StringComparer.OrdinalIgnoreCase);
    private List<StoredDeviceConfig> _storedDevices = [];
    private List<CarrierProfile> _carrierProfiles = [];
    private DeviceChangeOptions _changeOptions = new();
    private CancellationTokenSource? _pollCancellation;
    private Task? _pollTask;
    private CancellationTokenSource? _configSaveCancellation;
    private Task _configSaveTask = Task.CompletedTask;
    private CancellationTokenSource? _settingsSaveCancellation;
    private Task _settingsSaveTask = Task.CompletedTask;
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

    [ObservableProperty]
    private bool _isRandomizingSelectedDevices;

    [ObservableProperty]
    private bool _isBatchActionRunning;

    private DeviceRowViewModel? _selectedInfoDevice;

    public ChangeMultipleDevicesViewModel(
        IAddDevicesDialogService addDevicesDialogService,
        IAdvancedChangeConfigDialogService advancedChangeConfigDialogService,
        ICarrierDataService carrierDataService,
        IDeviceActionConfirmationDialogService deviceActionConfirmationDialogService,
        IDeviceChangeService deviceChangeService,
        IDeviceConfigService deviceConfigService,
        IDeviceListService deviceListService,
        IDeviceActionGuardService deviceActionGuardService,
        ILocalizationService localizationService,
        IMultipleDeviceConfigService multipleDeviceConfigService,
        IRandomDeviceInfoDialogService randomDeviceInfoDialogService,
        IRandomDeviceService randomDeviceService,
        ISimProfileService simProfileService,
        ISettingsService settingsService,
        IUiDispatcherService uiDispatcher,
        IPollingService pollingService,
        AppSettings settings,
        ILogger<ChangeMultipleDevicesViewModel> logger)
    {
        _addDevicesDialogService = addDevicesDialogService;
        _advancedChangeConfigDialogService = advancedChangeConfigDialogService;
        _carrierDataService = carrierDataService;
        _deviceActionConfirmationDialogService = deviceActionConfirmationDialogService;
        _deviceChangeService = deviceChangeService;
        _deviceConfigService = deviceConfigService;
        _deviceListService = deviceListService;
        _deviceActionGuardService = deviceActionGuardService;
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
        _deviceActionGuardService.BusyStateChanged += OnDeviceBusyStateChanged;
    }

    public ObservableCollection<DeviceRowViewModel> Devices { get; }
    public ObservableCollection<DeviceRowViewModel> SelectedDevices { get; }
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
            ViewRandomDeviceInfoCommand.NotifyCanExecuteChanged();
        }
    }

    public bool CanInteractWithSelectedInfoDevice =>
        SelectedInfoDevice == null || !IsDeviceBusy(SelectedInfoDevice);

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
        await FlushPendingDeviceEditsAsync().ConfigureAwait(false);
        await FlushPendingConfigurationSaveAsync().ConfigureAwait(false);
        await FlushPendingSettingsSaveAsync().ConfigureAwait(false);
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
        return !IsLoadingDevices && !IsBatchActionRunning;
    }

    [RelayCommand]
    private void ToggleDeviceSelection(DeviceRowViewModel? device)
    {
        if (device == null || IsDeviceBusy(device))
            return;

        device.IsSelected = !device.IsSelected;
    }

    [RelayCommand]
    private void ToggleSelectAllDevices()
    {
        DeviceRowViewModel[] editableDevices = _allDeviceRows
            .Where(device => !IsDeviceBusy(device))
            .ToArray();
        if (editableDevices.Length == 0)
            return;

        bool shouldSelect = editableDevices.Any(device => !device.IsSelected);
        _isBatchUpdatingSelection = true;
        try
        {
            foreach (DeviceRowViewModel device in editableDevices)
                device.IsSelected = shouldSelect;
        }
        finally
        {
            _isBatchUpdatingSelection = false;
        }

        SynchronizeSelectedDeviceSettings();
    }

    [RelayCommand(CanExecute = nameof(CanRandomizeSelectedDevices))]
    private async Task RandomSelectedDevicesAsync(CancellationToken cancellationToken)
    {
        if (!await _batchActionLock.WaitAsync(0, cancellationToken).ConfigureAwait(true))
            return;

        var leases = new List<(DeviceRowViewModel Device, IDisposable Lease)>();
        try
        {
            DeviceRowViewModel[] targets = _allDeviceRows
                .Where(device => device.IsSelected)
                .ToArray();
            if (targets.Length == 0)
                return;

            IsRandomizingSelectedDevices = true;
            IsBatchActionRunning = true;
            NotifySelectionChanged();
            RandomDeviceRequest request = CreateCurrentRandomDeviceRequest();

            try
            {
                foreach (DeviceRowViewModel device in targets)
                {
                    IDisposable? lease = _deviceActionGuardService.TryAcquire(device.Serial);
                    if (lease == null)
                    {
                        SetDeviceLog(device, "Log_ActionAlreadyInProgress");
                        continue;
                    }

                    leases.Add((device, lease));
                }

                using var throttle = new SemaphoreSlim(MaxConcurrentBatchActions, MaxConcurrentBatchActions);
                Task[] operations = leases
                    .Select(item => RandomSelectedDeviceAsync(
                        item.Device,
                        item.Lease,
                        request,
                        throttle,
                        cancellationToken))
                    .ToArray();
                await Task.WhenAll(operations).ConfigureAwait(true);
                leases.Clear();
            }
            catch
            {
                foreach ((_, IDisposable lease) in leases)
                    lease.Dispose();
                leases.Clear();
                throw;
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Failed to randomize selected devices in Multiple Device screen.");
        }
        finally
        {
            foreach ((_, IDisposable lease) in leases)
                lease.Dispose();

            IsRandomizingSelectedDevices = false;
            IsBatchActionRunning = false;
            NotifySelectionChanged();
            _batchActionLock.Release();
        }
    }

    private bool CanRandomizeSelectedDevices()
    {
        return !IsBatchActionRunning
               && _allDeviceRows.Any(device => device.IsSelected && !IsDeviceBusy(device));
    }

    [RelayCommand(CanExecute = nameof(CanRunSelectedDeviceBatchAction))]
    private Task ChangeSelectedDevicesAsync(CancellationToken cancellationToken)
    {
        return RunSelectedDeviceBatchActionAsync(MultipleDeviceBatchAction.ChangeAndWipe, cancellationToken);
    }

    [RelayCommand(CanExecute = nameof(CanRunSelectedDeviceBatchAction))]
    private Task ChangeSelectedDevicesWithoutWipeAsync(CancellationToken cancellationToken)
    {
        return RunSelectedDeviceBatchActionAsync(MultipleDeviceBatchAction.ChangeWithoutWipe, cancellationToken);
    }

    [RelayCommand(CanExecute = nameof(CanRunSelectedDeviceBatchAction))]
    private Task WipeSelectedDevicesWithoutChangeAsync(CancellationToken cancellationToken)
    {
        return RunSelectedDeviceBatchActionAsync(MultipleDeviceBatchAction.WipeWithoutChange, cancellationToken);
    }

    [RelayCommand(CanExecute = nameof(CanRunSelectedDeviceBatchAction))]
    private Task RandomSelectedSimsAsync(CancellationToken cancellationToken)
    {
        return RunSelectedDeviceBatchActionAsync(null, cancellationToken);
    }

    [RelayCommand(CanExecute = nameof(CanRunSelectedDeviceBatchAction))]
    private Task ChangeSelectedSimsAsync(CancellationToken cancellationToken)
    {
        return RunSelectedDeviceBatchActionAsync(MultipleDeviceBatchAction.ChangeSim, cancellationToken);
    }

    private bool CanRunSelectedDeviceBatchAction()
    {
        return !IsBatchActionRunning
               && _allDeviceRows.Any(device => device.IsSelected && !IsDeviceBusy(device));
    }

    private async Task RunSelectedDeviceBatchActionAsync(
        MultipleDeviceBatchAction? action,
        CancellationToken cancellationToken)
    {
        if (!await _batchActionLock.WaitAsync(0, cancellationToken).ConfigureAwait(true))
            return;

        var targets = new List<BatchActionTarget>();
        try
        {
            IsBatchActionRunning = true;
            NotifySelectionChanged();
            DeviceChangeOptions changeOptions = CreateCurrentChangeOptions();
            bool changeSimEnabled = IsChangeSimEnabled;
            CarrierCountryOption? country = SelectedCountry;
            CarrierOption? carrier = SelectedCarrier;

            foreach (DeviceRowViewModel device in _allDeviceRows.Where(device => device.IsSelected).ToArray())
            {
                if (IsDeviceBusy(device))
                {
                    SetDeviceLog(device, "Log_ActionAlreadyInProgress");
                    continue;
                }

                if (action != null
                    && action != MultipleDeviceBatchAction.ChangeSim
                    && device.ConnectionStatus != AdbDeviceStatus.Online)
                {
                    SetDeviceLog(device, "Log_DeviceMustBeOnline");
                    continue;
                }

                if (action == MultipleDeviceBatchAction.ChangeSim
                    && device.ConnectionStatus != AdbDeviceStatus.Online)
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

                SimProfile? simProfile = null;
                if (action == MultipleDeviceBatchAction.ChangeSim
                    && !_randomSimProfiles.TryGetValue(device.Serial, out simProfile))
                {
                    SetDeviceLog(device, "Log_RandomSimRequired");
                    continue;
                }

                IDisposable? lease = _deviceActionGuardService.TryAcquire(device.Serial);
                if (lease == null)
                {
                    SetDeviceLog(device, "Log_ActionAlreadyInProgress");
                    continue;
                }

                targets.Add(new BatchActionTarget(device, lease, deviceProfile, simProfile));
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
                    foreach (BatchActionTarget target in targets)
                        SetDeviceLog(target.Device, GetCanceledLogKey(action.Value));
                    return;
                }
            }

            using var throttle = new SemaphoreSlim(MaxConcurrentBatchActions, MaxConcurrentBatchActions);
            Task[] operations = targets
                .Select(target => ExecuteBatchActionTargetAsync(
                    action,
                    target,
                    changeOptions,
                    changeSimEnabled,
                    country,
                    carrier,
                    throttle,
                    cancellationToken))
                .ToArray();
            await Task.WhenAll(operations).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Failed to execute a Multiple Device batch action.");
        }
        finally
        {
            foreach (BatchActionTarget target in targets)
                target.Lease.Dispose();

            IsBatchActionRunning = false;
            NotifySelectionChanged();
            _batchActionLock.Release();
        }
    }

    private async Task ExecuteBatchActionTargetAsync(
        MultipleDeviceBatchAction? action,
        BatchActionTarget target,
        DeviceChangeOptions changeOptions,
        bool changeSimEnabled,
        CarrierCountryOption? country,
        CarrierOption? carrier,
        SemaphoreSlim throttle,
        CancellationToken cancellationToken)
    {
        try
        {
            await throttle.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await RunOnUiContextAsync(() => SetDeviceLog(target.Device, GetStartLogKey(action)))
                    .ConfigureAwait(false);
                IProgress<DeviceChangeStage> progress = CreateDeviceChangeProgress(target.Device, action);
                switch (action)
                {
                    case MultipleDeviceBatchAction.ChangeAndWipe:
                        await _deviceChangeService.ChangeAsync(
                                target.Device.Serial,
                                target.DeviceProfile!,
                                changeSimEnabled,
                                changeOptions,
                                progress,
                                cancellationToken)
                            .ConfigureAwait(false);
                        break;
                    case MultipleDeviceBatchAction.ChangeWithoutWipe:
                        await _deviceChangeService.ChangeWithoutWipeAsync(
                                target.Device.Serial,
                                target.DeviceProfile!,
                                changeSimEnabled,
                                changeOptions,
                                progress,
                                cancellationToken)
                            .ConfigureAwait(false);
                        break;
                    case MultipleDeviceBatchAction.WipeWithoutChange:
                        await _deviceChangeService.WipeWithoutChangeAsync(
                                target.Device.Serial,
                                changeOptions,
                                progress,
                                cancellationToken)
                            .ConfigureAwait(false);
                        break;
                    case MultipleDeviceBatchAction.ChangeSim:
                    {
                        SimProfile editedProfile = CreateEditedSimProfile(target.Device.Serial, target.SimProfile!);
                        await _deviceChangeService.ChangeSimAsync(
                                target.Device.Serial,
                                editedProfile,
                                cancellationToken)
                            .ConfigureAwait(false);
                        await RunOnUiContextAsync(() => ApplyRandomSimInfo(target.Device.Serial, editedProfile))
                            .ConfigureAwait(false);
                        break;
                    }
                    case null:
                    {
                        SimProfile randomSim = _simProfileService.CreateRandomProfile(country, carrier);
                        await RunOnUiContextAsync(() => ApplyRandomSimInfo(target.Device.Serial, randomSim))
                            .ConfigureAwait(false);
                        break;
                    }
                    default:
                        throw new ArgumentOutOfRangeException(nameof(action), action, null);
                }

                await RunOnUiContextAsync(() => SetDeviceLog(target.Device, GetSuccessLogKey(action)))
                    .ConfigureAwait(false);
            }
            finally
            {
                throttle.Release();
            }
        }
        catch (OperationCanceledException)
        {
            await RunOnUiContextAsync(() => SetDeviceLog(target.Device, "Log_Ready"))
                .ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Failed to execute {Action} for device {Serial}.", action, target.Device.Serial);
            await RunOnUiContextAsync(() => SetDeviceLog(target.Device, GetFailureLogKey(action)))
                .ConfigureAwait(false);
        }
        finally
        {
            target.Lease.Dispose();
        }
    }

    private DeviceChangeOptions CreateCurrentChangeOptions()
    {
        return DeviceChangeOptionsHelper.CreateNormalizedCopy(_changeOptions, UseDefaultChangeMode);
    }

    private IProgress<DeviceChangeStage> CreateDeviceChangeProgress(
        DeviceRowViewModel device,
        MultipleDeviceBatchAction? action)
    {
        return new Progress<DeviceChangeStage>(stage =>
            SetDeviceLog(device, stage switch
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

    private async Task RandomSelectedDeviceAsync(
        DeviceRowViewModel device,
        IDisposable lease,
        RandomDeviceRequest request,
        SemaphoreSlim throttle,
        CancellationToken cancellationToken)
    {
        try
        {
            await throttle.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await RunOnUiContextAsync(() => SetDeviceLog(device, "Log_RandomDevice"))
                    .ConfigureAwait(false);
                RandomDeviceResult result = await _randomDeviceService
                    .CreateRandomProfileAsync(request, cancellationToken)
                    .ConfigureAwait(false);

                await RunOnUiContextAsync(() =>
                {
                    switch (result.Status)
                    {
                        case RandomDeviceStatus.Created when result.Profile != null:
                            ApplyRandomDeviceInfo(device.Serial, result.Profile);
                            SetDeviceLog(device, "Log_RandomDeviceSuccess");
                            break;
                        case RandomDeviceStatus.LoginRequired:
                            SetDeviceLog(device, "Log_RandomDeviceLoginRequired");
                            break;
                        default:
                            SetDeviceLog(device, "Log_RandomDeviceFailed");
                            break;
                    }
                }).ConfigureAwait(false);
            }
            finally
            {
                throttle.Release();
            }
        }
        catch (OperationCanceledException)
        {
            await RunOnUiContextAsync(() => SetDeviceLog(device, "Log_Ready"))
                .ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Failed to randomize device {Serial}.", device.Serial);
            await RunOnUiContextAsync(() => SetDeviceLog(device, "Log_RandomDeviceFailed"))
                .ConfigureAwait(false);
        }
        finally
        {
            lease.Dispose();
        }
    }

    [RelayCommand]
    private async Task SaveMultipleDeviceColumnRatiosAsync(
        IReadOnlyDictionary<string, double>? ratios,
        CancellationToken cancellationToken)
    {
        if (ratios == null || ratios.Count == 0)
            return;

        _settings.ReplaceDeviceTableColumnRatios(ratios);

        OnPropertyChanged(nameof(DeviceTableColumnRatios));
        await SaveSettingsAsync(cancellationToken).ConfigureAwait(false);
    }

    [RelayCommand(CanExecute = nameof(CanOpenAdvancedChangeConfig))]
    private async Task OpenAdvancedChangeConfigAsync(CancellationToken cancellationToken)
    {
        DeviceRowViewModel? sourceDevice = GetFirstSelectedOnlineDevice();
        if (sourceDevice == null || UseDefaultChangeMode)
            return;

        try
        {
            AdvancedChangeConfigDialogResult? result =
                await _advancedChangeConfigDialogService
                    .ShowAdvancedChangeConfigAsync(
                        sourceDevice.Serial,
                        DeviceChangeOptionsHelper.CreateNormalizedCopy(
                            _changeOptions,
                            useDefaultMode: false),
                        UseIntegritySecurityPatch,
                        cancellationToken)
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
        return !UseDefaultChangeMode && GetFirstSelectedOnlineDevice() != null;
    }

    private DeviceRowViewModel? GetFirstSelectedOnlineDevice()
    {
        return _allDeviceRows.FirstOrDefault(device =>
            device.IsSelected
            && device.ConnectionStatus == AdbDeviceStatus.Online
            && !IsDeviceBusy(device));
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

        _isRefreshingRows = true;
        try
        {
            foreach (DeviceRowViewModel device in _allDeviceRows)
                device.PropertyChanged -= OnDeviceRowPropertyChanged;

            _allDeviceRows.Clear();
            for (int index = 0; index < storedDevices.Count; index++)
            {
                StoredDeviceConfig storedDevice = storedDevices[index];
                connectedBySerial.TryGetValue(storedDevice.Serial, out AdbDevice? connectedDevice);
                DeviceRowViewModel row = DeviceRowFactory.CreateDeviceRow(
                    index + 1,
                    storedDevice,
                    connectedDevice,
                    GetConnectionStatusText(connectedDevice?.Status ?? AdbDeviceStatus.Offline),
                    _localizationService.GetString("ChangeMultipleDevices_LogReady"));
                row.IsSelected = selectedSerials.Contains(row.Serial);
                row.IsActionBusy = _deviceActionGuardService.IsBusy(row.Serial);
                row.PropertyChanged += OnDeviceRowPropertyChanged;
                _allDeviceRows.Add(row);
            }

            ApplyDeviceFilterCore();
        }
        finally
        {
            _isRefreshingRows = false;
        }

        SynchronizeSelectedDeviceSettings();
        if (selectedInfoSerial != null)
        {
            DeviceRowViewModel? restoredInfoDevice = SelectedDevices.FirstOrDefault(device =>
                SerialEquals(device.Serial, selectedInfoSerial));
            if (restoredInfoDevice != null)
                SelectedInfoDevice = restoredInfoDevice;
        }
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
            return;
        }

        if (args.PropertyName == nameof(DeviceRowViewModel.Type))
        {
            CancelPendingDeviceEdit(deviceRow.Serial);
            TrackSilentSave(
                SaveDeviceRowEditAsync(deviceRow, GetActiveToken()),
                "Failed to save Multiple Device row edit.");
            ReapplySearchIfActive();
            return;
        }

        if (args.PropertyName == nameof(DeviceRowViewModel.ConnectionStatus))
            ApplyDeviceFilter();
    }

    private void OnDeviceBusyStateChanged(string serial, bool isBusy)
    {
        if (_isDisposed)
            return;

        void ApplyBusyState()
        {
            foreach (DeviceRowViewModel device in _allDeviceRows
                         .Where(device => SerialEquals(device.Serial, serial)))
            {
                device.IsActionBusy = isBusy;
            }

            if (SelectedInfoDevice != null
                && SerialEquals(SelectedInfoDevice.Serial, serial))
            {
                OnPropertyChanged(nameof(CanInteractWithSelectedInfoDevice));
            }

            NotifySelectionChanged();
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

    private bool IsDeviceBusy(DeviceRowViewModel device)
    {
        return _deviceActionGuardService.IsBusy(device.Serial);
    }

    private string GetLogText(string resourceKey)
    {
        return _localizationService.GetString(resourceKey);
    }

    private void SetDeviceLog(DeviceRowViewModel device, string resourceKey)
    {
        string message = GetLogText(resourceKey);
        device.Process = message;
        _logger.LogInformation("Multiple Device {Serial} action: {Message}", device.Serial, message);
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

    private void ApplyRandomDeviceInfo(string serial, DeviceInfoApiDevice randomDevice)
    {
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
            || !_randomDeviceProfiles.TryGetValue(SelectedInfoDevice.Serial, out DeviceInfoApiDevice? profile))
        {
            return;
        }

        CopyFormValuesToProfile(profile);
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

    private SimProfile CreateEditedSimProfile(string serial, SimProfile profile)
    {
        if (!_randomDeviceProfiles.TryGetValue(serial, out DeviceInfoApiDevice? deviceProfile))
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
        AddNewDevicesCommand.NotifyCanExecuteChanged();
        OpenAdvancedChangeConfigCommand.NotifyCanExecuteChanged();
        RandomSelectedDevicesCommand.NotifyCanExecuteChanged();
        ChangeSelectedDevicesCommand.NotifyCanExecuteChanged();
        ChangeSelectedDevicesWithoutWipeCommand.NotifyCanExecuteChanged();
        WipeSelectedDevicesWithoutChangeCommand.NotifyCanExecuteChanged();
        RandomSelectedSimsCommand.NotifyCanExecuteChanged();
        ChangeSelectedSimsCommand.NotifyCanExecuteChanged();
        ViewRandomDeviceInfoCommand.NotifyCanExecuteChanged();
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
                    .SaveAsync(CreateConfiguration(), CancellationToken.None)
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
        var pendingEdit = new PendingDeviceEdit(deviceRow, cancellation);
        lock (_pendingDeviceEditsLock)
        {
            if (_pendingDeviceEdits.Remove(deviceRow.Serial, out PendingDeviceEdit? previous))
                previous.Cancellation.Cancel();

            _pendingDeviceEdits[deviceRow.Serial] = pendingEdit;
            pendingEdit.PersistenceTask = PersistDeviceRowAfterDelayAsync(pendingEdit);
        }
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
                    pendingEdit.DeviceRow,
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
                        pendingEdit.DeviceRow.Serial,
                        out PendingDeviceEdit? current)
                    && ReferenceEquals(current, pendingEdit))
                {
                    _pendingDeviceEdits.Remove(pendingEdit.DeviceRow.Serial);
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
                    pendingEdit.DeviceRow,
                    CancellationToken.None)
                .ConfigureAwait(false);
        }
    }

    private async Task SaveDeviceRowEditAsync(
        DeviceRowViewModel deviceRow,
        CancellationToken cancellationToken)
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
                    selectedCountry: null,
                    selectedCarrier: null,
                    includeSelectedCarrierConfig: false,
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
        return SelectedInfoDevice != null
               && !IsDeviceBusy(SelectedInfoDevice)
               && _randomDeviceProfiles.ContainsKey(SelectedInfoDevice.Serial);
    }

    [RelayCommand(CanExecute = nameof(CanViewRandomDeviceInfo), AllowConcurrentExecutions = true)]
    private async Task ViewRandomDeviceInfoAsync(CancellationToken cancellationToken)
    {
        DeviceRowViewModel? device = SelectedInfoDevice;
        if (device == null
            || !_randomDeviceProfiles.TryGetValue(device.Serial, out DeviceInfoApiDevice? profile))
        {
            return;
        }

        IDisposable? lease = _deviceActionGuardService.TryAcquire(device.Serial);
        if (lease == null)
        {
            SetDeviceLog(device, "Log_ActionAlreadyInProgress");
            return;
        }

        using (lease)
        {
            try
            {
                bool updated = await _randomDeviceInfoDialogService
                    .ShowRandomDeviceInfoAsync(profile, cancellationToken)
                    .ConfigureAwait(true);
                if (updated && SelectedInfoDevice != null
                    && SerialEquals(SelectedInfoDevice.Serial, device.Serial))
                {
                    DisplayDeviceInfo(profile);
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Failed to open full random device info for {Serial}.", device.Serial);
            }
        }
    }

    private RandomDeviceRequest CreateCurrentRandomDeviceRequest()
    {
        return new RandomDeviceRequest
        {
            SelectedBrand = SelectedBrand,
            SelectedAndroidVersion = SelectedAndroidVersion,
            UseIntegritySecurityPatch = UseDefaultChangeMode || UseIntegritySecurityPatch,
            Country = SelectedCountry,
            Carrier = SelectedCarrier
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

        FlushPendingDeviceEditsAsync().GetAwaiter().GetResult();
        FlushPendingConfigurationSaveAsync().GetAwaiter().GetResult();
        FlushPendingSettingsSaveAsync().GetAwaiter().GetResult();
        _isDisposed = true;
        _pollCancellation?.Cancel();
        foreach (DeviceRowViewModel device in _allDeviceRows)
            device.PropertyChanged -= OnDeviceRowPropertyChanged;

        DeviceInfo.PropertyChanged -= OnDeviceInfoPropertyChanged;
        _deviceActionGuardService.BusyStateChanged -= OnDeviceBusyStateChanged;

        _pollCancellation?.Dispose();
        _pollCancellation = null;
    }

    private sealed class PendingDeviceEdit
    {
        public PendingDeviceEdit(
            DeviceRowViewModel deviceRow,
            CancellationTokenSource cancellation)
        {
            DeviceRow = deviceRow;
            Cancellation = cancellation;
        }

        public DeviceRowViewModel DeviceRow { get; }
        public CancellationTokenSource Cancellation { get; }
        public Task PersistenceTask { get; set; } = Task.CompletedTask;
    }

    private sealed class BatchActionTarget
    {
        public BatchActionTarget(
            DeviceRowViewModel device,
            IDisposable lease,
            DeviceInfoApiDevice? deviceProfile,
            SimProfile? simProfile)
        {
            Device = device;
            Lease = lease;
            DeviceProfile = deviceProfile;
            SimProfile = simProfile;
        }

        public DeviceRowViewModel Device { get; }
        public IDisposable Lease { get; }
        public DeviceInfoApiDevice? DeviceProfile { get; }
        public SimProfile? SimProfile { get; }
    }
}
