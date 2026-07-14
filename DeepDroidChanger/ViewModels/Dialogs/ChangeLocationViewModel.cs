using DeepDroidChanger.Models;
using DeepDroidChanger.Services;
using DeepDroidChanger.Helpers;
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

        private readonly IDeviceStoreService _deviceStoreService;
        private readonly ILocalizationService _localizationService;
        private readonly ILogger<ChangeLocationViewModel> _logger;
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
        private bool _isConfigMode = true;

        [ObservableProperty]
        private bool _isDeviceIpMode;

        [ObservableProperty]
        private string _latitude = string.Empty;

        [ObservableProperty]
        private string _longitude = string.Empty;

        public ChangeLocationViewModel(
            IDeviceStoreService deviceStoreService,
            ILocalizationService localizationService,
            ILogger<ChangeLocationViewModel> logger)
        {
            _deviceStoreService = deviceStoreService;
            _localizationService = localizationService;
            _logger = logger;
        }

        public event EventHandler<bool>? CloseRequested;

        public Task InitializeAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return LoadDeviceConfigAsync(cancellationToken);
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
                IsConfigMode = false;

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

        partial void OnDeviceSerialChanged(string value)
        {
            UpdateDeviceInfoText();
        }

        partial void OnDeviceNameChanged(string value) => UpdateDeviceInfoText();

        private void UpdateDeviceInfoText()
        {
            DeviceInfoText = DeviceInfoTextHelper.Create(_localizationService, DeviceName, DeviceSerial);
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
            var snapshot = new LocationConfigSnapshot(
                DeviceSerial,
                mode,
                IsConfigMode,
                Latitude,
                Longitude);

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
                        if (snapshot.IsConfigMode)
                        {
                            config.LocationLatitude = snapshot.Latitude;
                            config.LocationLongitude = snapshot.Longitude;
                        }
                    },
                    CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Failed to persist Change Location dialog settings.");
            }
        }

        private async Task LoadDeviceConfigAsync(CancellationToken cancellationToken)
        {
            try
            {
                _isInitializing = true;
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
                }
                else
                {
                    IsConfigMode = true;
                    IsDeviceIpMode = false;
                    Latitude = string.Empty;
                    Longitude = string.Empty;
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
            finally
            {
                _isInitializing = false;
            }
        }

        private readonly record struct LocationConfigSnapshot(
            string Serial,
            ChangeLocationMode Mode,
            bool IsConfigMode,
            string Latitude,
            string Longitude);
    }
}
