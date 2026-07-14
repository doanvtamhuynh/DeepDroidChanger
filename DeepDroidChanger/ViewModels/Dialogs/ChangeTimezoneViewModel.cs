using DeepDroidChanger.Services;
using DeepDroidChanger.Models;
using DeepDroidChanger.Helpers;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace DeepDroidChanger.ViewModels
{
    public sealed partial class ChangeTimezoneViewModel : ObservableObject
    {
        private readonly ITimezoneDataService _timezoneDataService;
        private readonly IDeviceStoreService _deviceStoreService;
        private readonly ISettingsService _settingsService;
        private readonly ILocalizationService _localizationService;
        private readonly AppSettings _appSettings;
        private readonly ILogger<ChangeTimezoneViewModel> _logger;
        private readonly object _configSaveLock = new();
        private Task _pendingConfigSave = Task.CompletedTask;
        private bool _isInitializing;

        [ObservableProperty]
        private string _deviceSerial = string.Empty;

        [ObservableProperty]
        private string _deviceName = string.Empty;

        [ObservableProperty]
        private string _deviceInfoText = string.Empty;

        [ObservableProperty]
        private string _deviceDataFilePath = string.Empty;

        [ObservableProperty]
        private bool _isDataMode = true;

        [ObservableProperty]
        private bool _isDeviceIpMode;

        [ObservableProperty]
        private TimezoneOption? _selectedTimezone;

        [ObservableProperty]
        private string _timezoneSearchText = string.Empty;

        [ObservableProperty]
        private bool _isTimezoneDropDownOpen;

        [ObservableProperty]
        private bool _isLoading = true;

        public ChangeTimezoneViewModel(
            ITimezoneDataService timezoneDataService,
            IDeviceStoreService deviceStoreService,
            ISettingsService settingsService,
            ILocalizationService localizationService,
            AppSettings appSettings,
            ILogger<ChangeTimezoneViewModel> logger)
        {
            _timezoneDataService = timezoneDataService;
            _deviceStoreService = deviceStoreService;
            _settingsService = settingsService;
            _localizationService = localizationService;
            _appSettings = appSettings;
            _logger = logger;

            AllTimezones = new ObservableCollection<TimezoneOption>();
            FilteredTimezones = new ObservableCollection<TimezoneOption>();

            DeviceDataFilePath = _appSettings.DeviceDataFilePath;
        }

        public event EventHandler<bool>? CloseRequested;

        public ObservableCollection<TimezoneOption> AllTimezones { get; }
        public ObservableCollection<TimezoneOption> FilteredTimezones { get; }

        public Task InitializeAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return LoadDataAsync(cancellationToken);
        }

        partial void OnIsDataModeChanged(bool value)
        {
            if (value)
                IsDeviceIpMode = false;

            SaveCommand.NotifyCanExecuteChanged();
            QueueConfigSave();
        }

        partial void OnIsDeviceIpModeChanged(bool value)
        {
            if (value)
            {
                IsDataMode = false;
                IsTimezoneDropDownOpen = false;
            }

            SaveCommand.NotifyCanExecuteChanged();
            QueueConfigSave();
        }

        partial void OnDeviceSerialChanged(string value)
        {
            UpdateDeviceInfoText();
        }

        partial void OnDeviceNameChanged(string value) => UpdateDeviceInfoText();

        private void UpdateDeviceInfoText()
        {
            DeviceInfoText = DeviceInfoTextHelper.Create(_localizationService, DeviceName, DeviceSerial);
        }

        partial void OnSelectedTimezoneChanged(TimezoneOption? value)
        {
            SaveCommand.NotifyCanExecuteChanged();
            QueueConfigSave();
        }

        partial void OnDeviceDataFilePathChanged(string value) => QueueConfigSave();

        partial void OnTimezoneSearchTextChanged(string value)
        {
            ApplyFilter(value);

            if (IsDataMode && !string.IsNullOrEmpty(value) && FilteredTimezones.Count > 0)
                IsTimezoneDropDownOpen = true;
        }

        public ChangeTimezoneDialogResult? BuildResult()
        {
            var mode = IsDeviceIpMode ? ChangeTimezoneMode.DeviceIp : ChangeTimezoneMode.Data;
            var timezone = SelectedTimezone?.Timezone ?? string.Empty;
            return new ChangeTimezoneDialogResult(mode, timezone);
        }

        private bool CanSave()
        {
            if (IsDeviceIpMode)
                return true;

            return IsDataMode && SelectedTimezone != null;
        }

        [RelayCommand(CanExecute = nameof(CanSave))]
        private async Task SaveAsync(CancellationToken cancellationToken)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                QueueConfigSave();
                await FlushPendingConfigSaveAsync().ConfigureAwait(true);
                CloseRequested?.Invoke(this, true);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Failed to save timezone settings.");
            }
        }

        public async Task FlushPendingConfigSaveAsync()
        {
            while (true)
            {
                Task pendingSave;
                lock (_configSaveLock)
                    pendingSave = _pendingConfigSave;

                await pendingSave.ConfigureAwait(true);

                lock (_configSaveLock)
                {
                    if (ReferenceEquals(pendingSave, _pendingConfigSave))
                        return;
                }
            }
        }

        private void QueueConfigSave()
        {
            if (_isInitializing || string.IsNullOrWhiteSpace(DeviceSerial))
                return;

            var mode = IsDeviceIpMode ? ChangeTimezoneMode.DeviceIp : ChangeTimezoneMode.Data;
            var snapshot = new TimezoneConfigSnapshot(
                DeviceSerial,
                mode,
                SelectedTimezone?.Timezone ?? string.Empty,
                DeviceDataFilePath);

            lock (_configSaveLock)
                _pendingConfigSave = PersistConfigAfterAsync(_pendingConfigSave, snapshot);
        }

        private async Task PersistConfigAfterAsync(Task previousSave, TimezoneConfigSnapshot snapshot)
        {
            try
            {
                await previousSave.ConfigureAwait(false);
                await _deviceStoreService.UpdateAsync(
                    snapshot.Serial,
                    config =>
                    {
                        config.TimezoneMode = snapshot.Mode.ToString();
                        config.Timezone = snapshot.Timezone;
                    },
                    CancellationToken.None).ConfigureAwait(false);

                _appSettings.DeviceDataFilePath = snapshot.DeviceDataFilePath;
                await _settingsService.SaveAsync(_appSettings, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Failed to persist Change Timezone dialog settings.");
            }
        }

        private string _lastTimezone = string.Empty;

        private async Task LoadDataAsync(CancellationToken cancellationToken)
        {
            try
            {
                _isInitializing = true;
                await LoadDeviceConfigAsync(cancellationToken);
                await LoadTimezonesAsync(cancellationToken);
            }
            finally
            {
                _isInitializing = false;
            }
        }

        private async Task LoadDeviceConfigAsync(CancellationToken cancellationToken)
        {
            try
            {
                var devices = await _deviceStoreService.LoadAsync(cancellationToken).ConfigureAwait(true);
                var config = devices.FirstOrDefault(device =>
                    string.Equals(device.Serial, DeviceSerial, StringComparison.OrdinalIgnoreCase));
                if (config != null)
                {
                    if (Enum.TryParse<ChangeTimezoneMode>(config.TimezoneMode, ignoreCase: true, out var mode))
                    {
                        IsDataMode = mode == ChangeTimezoneMode.Data;
                        IsDeviceIpMode = mode == ChangeTimezoneMode.DeviceIp;
                    }
                    else
                    {
                        IsDataMode = true;
                        IsDeviceIpMode = false;
                    }
                    _lastTimezone = config.Timezone ?? string.Empty;
                }
                else
                {
                    IsDataMode = true;
                    IsDeviceIpMode = false;
                    _lastTimezone = string.Empty;
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Failed to load device config.");
            }
        }

        private async Task LoadTimezonesAsync(CancellationToken cancellationToken)
        {
            try
            {
                var timezones = await _timezoneDataService.GetTimezonesAsync(cancellationToken).ConfigureAwait(true);

                AllTimezones.Clear();
                foreach (var tz in timezones)
                    AllTimezones.Add(tz);

                ApplyFilter(TimezoneSearchText);
                RestoreLastTimezone();
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Failed to load timezone options.");
            }
            finally
            {
                IsLoading = false;
            }
        }

        private void RestoreLastTimezone()
        {
            var match = string.IsNullOrWhiteSpace(_lastTimezone)
                ? null
                : AllTimezones.FirstOrDefault(tz => string.Equals(tz.Timezone, _lastTimezone, StringComparison.OrdinalIgnoreCase));

            if (match == null)
            {
                match = AllTimezones.FirstOrDefault(tz => string.Equals(tz.Timezone, "America/New_York", StringComparison.OrdinalIgnoreCase));
            }

            SelectedTimezone = match ?? FilteredTimezones.FirstOrDefault();
        }

        private void ApplyFilter(string? searchText)
        {
            FilteredTimezones.Clear();

            var query = searchText?.Trim() ?? string.Empty;

            foreach (var tz in AllTimezones)
            {
                if (query.Length == 0
                    || tz.DisplayText.Contains(query, StringComparison.OrdinalIgnoreCase)
                    || tz.Timezone.Contains(query, StringComparison.OrdinalIgnoreCase)
                    || tz.CountryName.Contains(query, StringComparison.OrdinalIgnoreCase)
                    || tz.CountryCode.Contains(query, StringComparison.OrdinalIgnoreCase))
                {
                    FilteredTimezones.Add(tz);
                }
            }

            if (SelectedTimezone != null && !FilteredTimezones.Contains(SelectedTimezone))
                SelectedTimezone = FilteredTimezones.FirstOrDefault();
        }

        private readonly record struct TimezoneConfigSnapshot(
            string Serial,
            ChangeTimezoneMode Mode,
            string Timezone,
            string DeviceDataFilePath);
    }
}
