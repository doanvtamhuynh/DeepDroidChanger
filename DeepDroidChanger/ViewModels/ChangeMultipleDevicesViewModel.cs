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
    private const string DefaultCountryIso = "us";

    private readonly IAddDevicesDialogService _addDevicesDialogService;
    private readonly IAdvancedChangeConfigDialogService _advancedChangeConfigDialogService;
    private readonly ICarrierDataService _carrierDataService;
    private readonly IDeviceConfigService _deviceConfigService;
    private readonly IDeviceListService _deviceListService;
    private readonly ILocalizationService _localizationService;
    private readonly IMultipleDeviceConfigService _multipleDeviceConfigService;
    private readonly ISettingsService _settingsService;
    private readonly IUiDispatcherService _uiDispatcher;
    private readonly IPollingService _pollingService;
    private readonly AppSettings _settings;
    private readonly ILogger<ChangeMultipleDevicesViewModel> _logger;
    private readonly SemaphoreSlim _deviceRefreshLock = new(1, 1);
    private readonly SemaphoreSlim _lifecycleLock = new(1, 1);
    private readonly object _pendingDeviceEditsLock = new();
    private readonly Dictionary<string, PendingDeviceEdit> _pendingDeviceEdits =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly object _pendingConfigSaveLock = new();
    private readonly object _pendingSettingsSaveLock = new();
    private readonly List<DeviceRowViewModel> _allDeviceRows = [];
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
    private bool _isDisposed;

    [ObservableProperty]
    private bool _isLoadingDevices;

    [ObservableProperty]
    private string _newDeviceCountText = string.Empty;

    [ObservableProperty]
    private string _selectedDeviceFilter = "All";

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
        IDeviceConfigService deviceConfigService,
        IDeviceListService deviceListService,
        ILocalizationService localizationService,
        IMultipleDeviceConfigService multipleDeviceConfigService,
        ISettingsService settingsService,
        IUiDispatcherService uiDispatcher,
        IPollingService pollingService,
        AppSettings settings,
        ILogger<ChangeMultipleDevicesViewModel> logger)
    {
        _addDevicesDialogService = addDevicesDialogService;
        _advancedChangeConfigDialogService = advancedChangeConfigDialogService;
        _carrierDataService = carrierDataService;
        _deviceConfigService = deviceConfigService;
        _deviceListService = deviceListService;
        _localizationService = localizationService;
        _multipleDeviceConfigService = multipleDeviceConfigService;
        _settingsService = settingsService;
        _uiDispatcher = uiDispatcher;
        _pollingService = pollingService;
        _settings = settings;
        _logger = logger;

        Devices = [];
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
    }

    public ObservableCollection<DeviceRowViewModel> Devices { get; }
    public DeviceInfoFormViewModel DeviceInfo { get; } = new();
    public ObservableCollection<CarrierCountryOption> Countries { get; }
    public ObservableCollection<CarrierOption> Carriers { get; }
    public ObservableCollection<string> AndroidVersions { get; }
    public IReadOnlyList<string> Brands { get; }
    public IReadOnlyList<string> TypeOptions { get; }
    public IReadOnlyDictionary<string, double> MultipleDeviceTableColumnRatios =>
        _settings.MultipleDeviceTableColumnRatios;

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
        return !IsLoadingDevices;
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
        bool shouldSelect = AllDevicesSelectionState != true;
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

    [RelayCommand]
    private async Task SaveMultipleDeviceColumnRatiosAsync(
        IReadOnlyDictionary<string, double>? ratios,
        CancellationToken cancellationToken)
    {
        if (ratios == null || ratios.Count == 0)
            return;

        _settings.MultipleDeviceTableColumnRatios =
            new Dictionary<string, double>(ratios, StringComparer.Ordinal);
        OnPropertyChanged(nameof(MultipleDeviceTableColumnRatios));
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
            device.IsSelected && device.ConnectionStatus == AdbDeviceStatus.Online);
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
        return SelectedDeviceFilter switch
        {
            "Online" => device.ConnectionStatus == AdbDeviceStatus.Online,
            "Offline" => device.ConnectionStatus != AdbDeviceStatus.Online,
            "Active" => string.Equals(device.Active, "Active", StringComparison.OrdinalIgnoreCase),
            "Inactive" => string.Equals(device.Active, "Inactive", StringComparison.OrdinalIgnoreCase),
            _ => true
        };
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
            return;
        }

        if (args.PropertyName == nameof(DeviceRowViewModel.Type))
        {
            CancelPendingDeviceEdit(deviceRow.Serial);
            TrackSilentSave(
                SaveDeviceRowEditAsync(deviceRow, GetActiveToken()),
                "Failed to save Multiple Device row edit.");
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

        NotifySelectionChanged();
    }

    private void NotifySelectionChanged()
    {
        OnPropertyChanged(nameof(AllDevicesSelectionState));
        OpenAdvancedChangeConfigCommand.NotifyCanExecuteChanged();
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
        foreach (DeviceRowViewModel device in _allDeviceRows)
        {
            device.ConnectionStatus =
                connectedBySerial.TryGetValue(device.Serial, out AdbDevice? connectedDevice)
                    ? connectedDevice.Status
                    : AdbDeviceStatus.Offline;
            device.Status = GetConnectionStatusText(device.ConnectionStatus);
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
}
