using DeepDroidChanger.Models;
using DeepDroidChanger.Services;
using DeepDroidChanger.Helpers;
using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace DeepDroidChanger.ViewModels
{
    public sealed partial class ChangeLocationViewModel : ObservableObject
    {
        private const double MinLatitude = -90d;
        private const double MaxLatitude = 90d;
        private const double MinLongitude = -180d;
        private const double MaxLongitude = 180d;

        private readonly ILocationDataService _locationDataService;
        private readonly IDeviceStoreService _deviceStoreService;
        private readonly ILocalizationService _localizationService;
        private readonly ILogger<ChangeLocationViewModel> _logger;
        private readonly object _configSaveLock = new();
        private Task _pendingConfigSave = Task.CompletedTask;
        private bool _isInitializing;
        private bool _isUpdatingCountryFromLocation;
        private string _lastCountryCode = string.Empty;
        private string _lastCityName = string.Empty;

        [ObservableProperty]
        private string _deviceSerial = string.Empty;

        [ObservableProperty]
        private string _deviceName = string.Empty;

        [ObservableProperty]
        private string _deviceInfoText = string.Empty;

        [ObservableProperty]
        private bool _isConfigMode = true;

        [ObservableProperty]
        private bool _isDeviceIpMode;

        [ObservableProperty]
        private string _latitude = string.Empty;

        [ObservableProperty]
        private string _longitude = string.Empty;

        [ObservableProperty]
        private CountryOption? _selectedCountry;

        [ObservableProperty]
        private LocationOption? _selectedLocation;

        [ObservableProperty]
        private string _locationSearchText = string.Empty;

        [ObservableProperty]
        private bool _isCountryDropDownOpen;

        [ObservableProperty]
        private bool _isLoading = true;

        public ChangeLocationViewModel(
            ILocationDataService locationDataService,
            IDeviceStoreService deviceStoreService,
            ILocalizationService localizationService,
            ILogger<ChangeLocationViewModel> logger)
        {
            _locationDataService = locationDataService;
            _deviceStoreService = deviceStoreService;
            _localizationService = localizationService;
            _logger = logger;

            AllLocations = new ObservableCollection<LocationOption>();
            Countries = new ObservableCollection<CountryOption>();
            FilteredCountries = new ObservableCollection<CountryOption>();
            CountryLocations = new ObservableCollection<LocationOption>();
        }

        public event EventHandler<bool>? CloseRequested;

        public ObservableCollection<LocationOption> AllLocations { get; }
        public ObservableCollection<CountryOption> Countries { get; }
        public ObservableCollection<CountryOption> FilteredCountries { get; }
        public ObservableCollection<LocationOption> CountryLocations { get; }

        public Task InitializeAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return LoadDataAsync(cancellationToken);
        }

        partial void OnIsConfigModeChanged(bool value)
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
                IsConfigMode = false;
                IsCountryDropDownOpen = false;
            }

            SaveCommand.NotifyCanExecuteChanged();
            QueueConfigSave();
        }

        partial void OnLatitudeChanged(string value)
        {
            SaveCommand.NotifyCanExecuteChanged();
            QueueConfigSave();
        }

        partial void OnLongitudeChanged(string value)
        {
            SaveCommand.NotifyCanExecuteChanged();
            QueueConfigSave();
        }

        partial void OnDeviceSerialChanged(string value) => UpdateDeviceInfoText();

        partial void OnDeviceNameChanged(string value) => UpdateDeviceInfoText();

        private void UpdateDeviceInfoText()
        {
            DeviceInfoText = DeviceInfoTextHelper.Create(_localizationService, DeviceName, DeviceSerial);
        }

        partial void OnSelectedCountryChanged(CountryOption? value)
        {
            if (value != null)
            {
                _lastCountryCode = value.CountryCode;
                QueueConfigSave();
            }

            if (_isUpdatingCountryFromLocation)
                return;

            PopulateCountryLocations(value);
        }

        partial void OnSelectedLocationChanged(LocationOption? value)
        {
            if (value != null)
            {
                _lastCityName = value.CityName;
                _lastCountryCode = value.CountryCode;
            }

            if (!_isInitializing && value != null && IsConfigMode)
            {
                Latitude = value.LatitudeString;
                Longitude = value.LongitudeString;
            }

            if (_isUpdatingCountryFromLocation)
                return;

            _isUpdatingCountryFromLocation = true;
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
                _isUpdatingCountryFromLocation = false;
            }

            SaveCommand.NotifyCanExecuteChanged();
            QueueConfigSave();
        }

        partial void OnLocationSearchTextChanged(string value)
        {
            ApplyCountryFilter(value, updateSelection: false);
            if (IsConfigMode)
            {
                bool shouldBeOpen = !string.IsNullOrWhiteSpace(value) && FilteredCountries.Count > 0;
                if (IsCountryDropDownOpen != shouldBeOpen)
                {
                    IsCountryDropDownOpen = shouldBeOpen;
                }
            }
        }

        [RelayCommand]
        public void ApplySelectedLocationCoordinates()
        {
            if (!_isInitializing && SelectedLocation != null && IsConfigMode)
            {
                Latitude = SelectedLocation.LatitudeString;
                Longitude = SelectedLocation.LongitudeString;
            }
        }

        public ChangeLocationDialogResult? BuildResult()
        {
            var mode = IsDeviceIpMode ? ChangeLocationMode.DeviceIp : ChangeLocationMode.Config;
            return new ChangeLocationDialogResult(mode, Latitude, Longitude);
        }

        private bool CanSave()
        {
            if (IsDeviceIpMode)
                return true;

            return IsConfigMode &&
                   double.TryParse(Latitude, NumberStyles.Float, CultureInfo.InvariantCulture, out var lat) &&
                   lat >= MinLatitude && lat <= MaxLatitude &&
                   double.TryParse(Longitude, NumberStyles.Float, CultureInfo.InvariantCulture, out var lon) &&
                   lon >= MinLongitude && lon <= MaxLongitude;
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
                _logger.LogError(exception, "Failed to save location settings.");
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

            var mode = IsDeviceIpMode ? ChangeLocationMode.DeviceIp : ChangeLocationMode.Config;
            var countryCode = SelectedCountry?.CountryCode;
            if (string.IsNullOrEmpty(countryCode))
                countryCode = _lastCountryCode;

            var cityName = SelectedLocation?.CityName;
            if (string.IsNullOrEmpty(cityName))
                cityName = _lastCityName;

            var snapshot = new LocationConfigSnapshot(
                DeviceSerial,
                mode,
                IsConfigMode,
                Latitude,
                Longitude,
                countryCode ?? string.Empty,
                cityName ?? string.Empty);

            lock (_configSaveLock)
                _pendingConfigSave = PersistConfigAfterAsync(_pendingConfigSave, snapshot);
        }

        private async Task PersistConfigAfterAsync(Task previousSave, LocationConfigSnapshot snapshot)
        {
            try
            {
                await previousSave.ConfigureAwait(false);
                await _deviceStoreService.UpdateAsync(
                    snapshot.Serial,
                    config =>
                    {
                        config.LocationMode = snapshot.Mode.ToString();
                        config.LocationLatitude = snapshot.Latitude;
                        config.LocationLongitude = snapshot.Longitude;

                        if (!string.IsNullOrEmpty(snapshot.CountryCode))
                        {
                            config.LocationCountryCode = snapshot.CountryCode;
                        }

                        if (!string.IsNullOrEmpty(snapshot.CityName))
                        {
                            config.LocationCityName = snapshot.CityName;
                        }
                    },
                    CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Failed to persist Change Location dialog settings.");
            }
        }

        private async Task LoadDataAsync(CancellationToken cancellationToken)
        {
            try
            {
                _isInitializing = true;
                await LoadDeviceConfigAsync(cancellationToken);
                await LoadLocationsAsync(cancellationToken);
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
                    if (Enum.TryParse<ChangeLocationMode>(config.LocationMode, ignoreCase: true, out var mode))
                    {
                        IsConfigMode = mode == ChangeLocationMode.Config;
                        IsDeviceIpMode = mode == ChangeLocationMode.DeviceIp;
                    }
                    else
                    {
                        IsConfigMode = true;
                        IsDeviceIpMode = false;
                    }

                    Latitude = config.LocationLatitude ?? string.Empty;
                    Longitude = config.LocationLongitude ?? string.Empty;
                    _lastCountryCode = config.LocationCountryCode ?? string.Empty;
                    _lastCityName = config.LocationCityName ?? string.Empty;
                }
                else
                {
                    IsConfigMode = true;
                    IsDeviceIpMode = false;
                    Latitude = string.Empty;
                    Longitude = string.Empty;
                    _lastCountryCode = string.Empty;
                    _lastCityName = string.Empty;
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

        private async Task LoadLocationsAsync(CancellationToken cancellationToken)
        {
            try
            {
                var locations = await _locationDataService.GetLocationsAsync(cancellationToken).ConfigureAwait(true);

                AllLocations.Clear();
                foreach (var loc in locations)
                    AllLocations.Add(loc);

                PopulateCountries();
                ApplyCountryFilter(LocationSearchText, updateSelection: false);
                RestoreLastLocation();
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Failed to load location options.");
            }
            finally
            {
                IsLoading = false;
            }
        }

        private void PopulateCountries()
        {
            Countries.Clear();

            var distinctCountries = AllLocations
                .GroupBy(loc => loc.CountryName, StringComparer.OrdinalIgnoreCase)
                .Select(group => new CountryOption(group.First().CountryCode, group.Key))
                .OrderBy(c => c.CountryName, StringComparer.OrdinalIgnoreCase);

            foreach (var country in distinctCountries)
                Countries.Add(country);
        }

        private void PopulateCountryLocations(CountryOption? country)
        {
            CountryLocations.Clear();
            if (country == null)
            {
                SelectedLocation = null;
                return;
            }

            var locations = AllLocations
                .Where(loc => string.Equals(loc.CountryName, country.CountryName, StringComparison.OrdinalIgnoreCase))
                .OrderBy(loc => loc.CityName, StringComparer.OrdinalIgnoreCase);

            foreach (var loc in locations)
                CountryLocations.Add(loc);

            // Select location by saved city name if present, otherwise default to first city in country
            if (!string.IsNullOrWhiteSpace(_lastCityName))
            {
                var cityMatch = CountryLocations.FirstOrDefault(loc =>
                    string.Equals(loc.CityName, _lastCityName, StringComparison.OrdinalIgnoreCase));
                if (cityMatch != null)
                {
                    SelectedLocation = cityMatch;
                    return;
                }
            }

            SelectedLocation = CountryLocations.FirstOrDefault();
        }

        private void RestoreLastLocation()
        {
            // Match saved country code directly
            CountryOption? countryMatch = null;
            if (!string.IsNullOrWhiteSpace(_lastCountryCode))
            {
                countryMatch = Countries.FirstOrDefault(c =>
                    string.Equals(c.CountryCode, _lastCountryCode, StringComparison.OrdinalIgnoreCase));
            }

            if (countryMatch != null)
            {
                SelectedCountry = countryMatch;
            }
            else if (Countries.Count > 0)
            {
                SelectedCountry = Countries.FirstOrDefault();
            }

            // Match saved city name directly
            if (!string.IsNullOrWhiteSpace(_lastCityName) && CountryLocations.Count > 0)
            {
                var cityMatch = CountryLocations.FirstOrDefault(loc =>
                    string.Equals(loc.CityName, _lastCityName, StringComparison.OrdinalIgnoreCase));
                if (cityMatch != null)
                {
                    SelectedLocation = cityMatch;
                }
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

        private readonly record struct LocationConfigSnapshot(
            string Serial,
            ChangeLocationMode Mode,
            bool IsConfigMode,
            string Latitude,
            string Longitude,
            string CountryCode,
            string CityName);
    }
}
