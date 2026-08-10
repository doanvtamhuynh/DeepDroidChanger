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
        private readonly ILocationDataService _locationDataService;
        private readonly IDeviceStoreService _deviceStoreService;
        private readonly ILocalizationService _localizationService;
        private readonly ILogger<ChangeTimezoneViewModel> _logger;
        private readonly object _configSaveLock = new();
        private Task _pendingConfigSave = Task.CompletedTask;
        private bool _isInitializing;
        private bool _isUpdatingCountryFromTimezone;

        [ObservableProperty]
        private string _deviceSerial = string.Empty;

        [ObservableProperty]
        private string _deviceName = string.Empty;

        [ObservableProperty]
        private string _deviceInfoText = string.Empty;

        [ObservableProperty]
        private bool _isBatchMode;

        [ObservableProperty]
        private int _batchTargetCount;

        [ObservableProperty]
        private bool _isDataMode = true;

        [ObservableProperty]
        private bool _isDeviceIpMode;

        [ObservableProperty]
        private CountryOption? _selectedCountry;

        [ObservableProperty]
        private TimezoneOption? _selectedTimezone;

        [ObservableProperty]
        private string _timezoneSearchText = string.Empty;

        [ObservableProperty]
        private bool _isCountryDropDownOpen;

        [ObservableProperty]
        private bool _isLoading = true;

        public ChangeTimezoneViewModel(
            ILocationDataService locationDataService,
            IDeviceStoreService deviceStoreService,
            ILocalizationService localizationService,
            ILogger<ChangeTimezoneViewModel> logger)
        {
            _locationDataService = locationDataService;
            _deviceStoreService = deviceStoreService;
            _localizationService = localizationService;
            _logger = logger;

            AllTimezones = new ObservableCollection<TimezoneOption>();
            Countries = new ObservableCollection<CountryOption>();
            FilteredCountries = new ObservableCollection<CountryOption>();
            CountryTimezones = new ObservableCollection<TimezoneOption>();
        }

        public event EventHandler<bool>? CloseRequested;

        public ObservableCollection<TimezoneOption> AllTimezones { get; }
        public ObservableCollection<CountryOption> Countries { get; }
        public ObservableCollection<CountryOption> FilteredCountries { get; }
        public ObservableCollection<TimezoneOption> CountryTimezones { get; }

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

        partial void OnIsBatchModeChanged(bool value)
        {
            UpdateDeviceInfoText();
            QueueConfigSave();
            SaveCommand.NotifyCanExecuteChanged();
        }

        partial void OnBatchTargetCountChanged(int value) => UpdateDeviceInfoText();

        partial void OnIsDeviceIpModeChanged(bool value)
        {
            if (value)
            {
                IsDataMode = false;
                IsCountryDropDownOpen = false;
            }

            SaveCommand.NotifyCanExecuteChanged();
            QueueConfigSave();
        }

        partial void OnDeviceSerialChanged(string value) => UpdateDeviceInfoText();

        partial void OnDeviceNameChanged(string value) => UpdateDeviceInfoText();

        private void UpdateDeviceInfoText()
        {
            if (IsBatchMode)
            {
                string format = _localizationService.GetString("ChangeTimezone_BatchDeviceInfo");
                try
                {
                    DeviceInfoText = format.Contains("{0}", StringComparison.Ordinal)
                        ? string.Format(format, BatchTargetCount)
                        : $"{format} ({BatchTargetCount})";
                }
                catch (FormatException)
                {
                    DeviceInfoText = $"{format} ({BatchTargetCount})";
                }
                return;
            }

            DeviceInfoText = DeviceInfoTextHelper.Create(_localizationService, DeviceName, DeviceSerial);
        }

        partial void OnSelectedCountryChanged(CountryOption? value)
        {
            if (_isUpdatingCountryFromTimezone)
                return;

            PopulateCountryTimezones(value);
        }

        partial void OnSelectedTimezoneChanged(TimezoneOption? value)
        {
            if (_isUpdatingCountryFromTimezone)
                return;

            _isUpdatingCountryFromTimezone = true;
            try
            {
                if (value != null && (SelectedCountry == null || !string.Equals(SelectedCountry.CountryName, value.CountryName, StringComparison.OrdinalIgnoreCase)))
                {
                    var matchingCountry = FilteredCountries.FirstOrDefault(c => string.Equals(c.CountryName, value.CountryName, StringComparison.OrdinalIgnoreCase))
                                         ?? Countries.FirstOrDefault(c => string.Equals(c.CountryName, value.CountryName, StringComparison.OrdinalIgnoreCase));
                    if (matchingCountry != null)
                    {
                        SelectedCountry = matchingCountry;
                    }
                }
            }
            finally
            {
                _isUpdatingCountryFromTimezone = false;
            }

            SaveCommand.NotifyCanExecuteChanged();
            QueueConfigSave();
        }

        partial void OnTimezoneSearchTextChanged(string value)
        {
            ApplyCountryFilter(value, updateSelection: false);
            if (IsDataMode)
            {
                bool shouldBeOpen = !string.IsNullOrWhiteSpace(value) && FilteredCountries.Count > 0;
                if (IsCountryDropDownOpen != shouldBeOpen)
                {
                    IsCountryDropDownOpen = shouldBeOpen;
                }
            }
        }

        public ChangeTimezoneDialogResult? BuildResult()
        {
            var mode = IsDeviceIpMode ? ChangeTimezoneMode.DeviceIp : ChangeTimezoneMode.Data;
            var timezone = SelectedTimezone?.Timezone ?? string.Empty;
            if (IsBatchMode && mode == ChangeTimezoneMode.Data && SelectedTimezone == null)
                return null;

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
                if (!IsBatchMode)
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
            if (_isInitializing || IsBatchMode || string.IsNullOrWhiteSpace(DeviceSerial))
                return;

            var mode = IsDeviceIpMode ? ChangeTimezoneMode.DeviceIp : ChangeTimezoneMode.Data;
            var snapshot = new TimezoneConfigSnapshot(
                DeviceSerial,
                mode,
                SelectedTimezone?.Timezone ?? string.Empty);

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
                if (!IsBatchMode)
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
                var timezones = await _locationDataService.GetTimezonesAsync(cancellationToken).ConfigureAwait(true);

                AllTimezones.Clear();
                foreach (var tz in timezones)
                    AllTimezones.Add(tz);

                PopulateCountries();
                ApplyCountryFilter(TimezoneSearchText);
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

        private void PopulateCountries()
        {
            Countries.Clear();

            var distinctCountries = AllTimezones
                .GroupBy(tz => tz.CountryName, StringComparer.OrdinalIgnoreCase)
                .Select(group => new CountryOption(group.First().CountryCode, group.Key))
                .OrderBy(c => c.CountryName, StringComparer.OrdinalIgnoreCase);

            foreach (var country in distinctCountries)
                Countries.Add(country);
        }

        private void PopulateCountryTimezones(CountryOption? country)
        {
            CountryTimezones.Clear();
            if (country == null)
            {
                SelectedTimezone = null;
                return;
            }

            var timezones = AllTimezones
                .Where(tz => string.Equals(tz.CountryName, country.CountryName, StringComparison.OrdinalIgnoreCase))
                .OrderBy(tz => tz.Timezone, StringComparer.OrdinalIgnoreCase);

            foreach (var tz in timezones)
                CountryTimezones.Add(tz);

            if (SelectedTimezone != null && CountryTimezones.Any(tz => string.Equals(tz.Timezone, SelectedTimezone.Timezone, StringComparison.OrdinalIgnoreCase)))
            {
                SelectedTimezone = CountryTimezones.First(tz => string.Equals(tz.Timezone, SelectedTimezone.Timezone, StringComparison.OrdinalIgnoreCase));
                return;
            }

            if (!string.IsNullOrWhiteSpace(_lastTimezone))
            {
                var restoredMatch = CountryTimezones.FirstOrDefault(tz => string.Equals(tz.Timezone, _lastTimezone, StringComparison.OrdinalIgnoreCase));
                if (restoredMatch != null)
                {
                    SelectedTimezone = restoredMatch;
                    return;
                }
            }

            SelectedTimezone = CountryTimezones.FirstOrDefault();
        }

        private void RestoreLastTimezone()
        {
            var match = string.IsNullOrWhiteSpace(_lastTimezone)
                ? null
                : AllTimezones.FirstOrDefault(tz => string.Equals(tz.Timezone, _lastTimezone, StringComparison.OrdinalIgnoreCase));

            if (match != null)
            {
                SelectedCountry = Countries.FirstOrDefault(c => string.Equals(c.CountryName, match.CountryName, StringComparison.OrdinalIgnoreCase));
                SelectedTimezone = CountryTimezones.FirstOrDefault(tz => string.Equals(tz.Timezone, match.Timezone, StringComparison.OrdinalIgnoreCase))
                                   ?? CountryTimezones.FirstOrDefault();
            }
            else
            {
                SelectedCountry = Countries.FirstOrDefault();
            }
        }

        private void ApplyCountryFilter(string? searchText, bool updateSelection = true)
        {
            FilteredCountries.Clear();
            var query = searchText?.Trim() ?? string.Empty;

            foreach (var country in Countries)
            {
                if (query.Length == 0
                    || country.CountryDisplayText.Contains(query, StringComparison.OrdinalIgnoreCase))
                {
                    FilteredCountries.Add(country);
                }
            }

            if (updateSelection)
            {
                if (SelectedCountry == null || !FilteredCountries.Any(c => string.Equals(c.CountryName, SelectedCountry.CountryName, StringComparison.OrdinalIgnoreCase)))
                {
                    SelectedCountry = FilteredCountries.FirstOrDefault();
                }
            }
        }

        private readonly record struct TimezoneConfigSnapshot(
            string Serial,
            ChangeTimezoneMode Mode,
            string Timezone);
    }
}
