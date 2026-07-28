using DeepDroidChanger.Services;
using DeepDroidChanger.Models;
using DeepDroidChanger.Helpers;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace DeepDroidChanger.ViewModels
{
    public sealed partial class FakeProxyViewModel : ObservableObject
    {
        private const string SocksScheme = "socks5://";
        private const string SchemeMarker = "://";
        private const char AuthSeparator = '@';
        private const char ProxySegmentSeparator = ':';
        private const int MinProxyPort = 1;
        private const int MaxProxyPort = 65535;

        private readonly IDeviceStoreService _deviceStoreService;
        private readonly ILocalizationService _localizationService;
        private readonly ILogger<FakeProxyViewModel> _logger;
        private readonly object _configSaveLock = new();
        private Task _pendingConfigSave = Task.CompletedTask;
        private bool _isInitializing;
        private bool _isSyncing;

        [ObservableProperty]
        private string _deviceSerial = string.Empty;

        [ObservableProperty]
        private string _deviceName = string.Empty;

        [ObservableProperty]
        private string _deviceInfoText = string.Empty;

        [ObservableProperty]
        private string _fullProxyString = string.Empty;

        [ObservableProperty]
        private string _proxyHost = string.Empty;

        [ObservableProperty]
        private string _proxyPort = string.Empty;

        [ObservableProperty]
        private string _proxyUsername = string.Empty;

        [ObservableProperty]
        private string _proxyPassword = string.Empty;

        [ObservableProperty]
        private string _selectedProxyType = "Socks 5";

        [ObservableProperty]
        private bool _proxyChangeLocationByIp = true;

        [ObservableProperty]
        private bool _proxyChangeTimezoneByIp = true;

        public IReadOnlyList<string> ProxyTypes { get; } = new[] { "Socks 5" };

        public event EventHandler<bool>? CloseRequested;

        public FakeProxyViewModel(
            IDeviceStoreService deviceStoreService,
            ILocalizationService localizationService,
            ILogger<FakeProxyViewModel> logger)
        {
            _deviceStoreService = deviceStoreService;
            _localizationService = localizationService;
            _logger = logger;
        }

        public Task InitializeAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return LoadDeviceConfigAsync(cancellationToken);
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
                    SelectedProxyType = string.IsNullOrWhiteSpace(config.ProxyType) ? "Socks 5" : config.ProxyType;
                    FullProxyString = config.ProxyFullString ?? string.Empty;
                    ProxyChangeLocationByIp = config.ProxyChangeLocationByIp;
                    ProxyChangeTimezoneByIp = config.ProxyChangeTimezoneByIp;
                    ParseProxyString(FullProxyString);
                    RefreshFullProxyStringFromParts();
                }
                else
                {
                    SelectedProxyType = "Socks 5";
                    FullProxyString = string.Empty;
                    ProxyChangeLocationByIp = true;
                    ProxyChangeTimezoneByIp = true;
                    ParseProxyString(string.Empty);
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
                SaveCommand.NotifyCanExecuteChanged();
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

            var shouldPersistProxy = CanSave() || AreProxyInputsEmpty();
            var snapshot = new ProxyConfigSnapshot(
                DeviceSerial,
                shouldPersistProxy,
                CanSave()
                    ? FormatProxyDisplayString(ProxyHost, ProxyPort, ProxyUsername, ProxyPassword)
                    : string.Empty,
                SelectedProxyType,
                ProxyChangeLocationByIp,
                ProxyChangeTimezoneByIp);

            lock (_configSaveLock)
                _pendingConfigSave = PersistConfigAfterAsync(_pendingConfigSave, snapshot);
        }

        private async Task PersistConfigAfterAsync(Task previousSave, ProxyConfigSnapshot snapshot)
        {
            try
            {
                await previousSave.ConfigureAwait(false);
                await _deviceStoreService.UpdateAsync(
                    snapshot.Serial,
                    config =>
                    {
                        if (snapshot.ShouldPersistProxy)
                        {
                            config.ProxyFullString = snapshot.FullProxyString;
                            config.ProxyType = snapshot.ProxyType;
                        }

                        config.ProxyChangeLocationByIp = snapshot.ChangeLocationByIp;
                        config.ProxyChangeTimezoneByIp = snapshot.ChangeTimezoneByIp;
                    },
                    CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Failed to persist Fake Proxy dialog settings.");
            }
        }

        partial void OnFullProxyStringChanged(string value)
        {
            if (!_isInitializing && !_isSyncing)
            {
                _isSyncing = true;
                ParseProxyString(value);
                if (CanSave())
                {
                    FullProxyString = FormatProxyDisplayString(ProxyHost, ProxyPort, ProxyUsername, ProxyPassword);
                }
                else if (string.IsNullOrWhiteSpace(value))
                {
                    FullProxyString = string.Empty;
                }

                _isSyncing = false;
                QueueConfigSave();
                SaveCommand.NotifyCanExecuteChanged();
            }
        }

        partial void OnProxyHostChanged(string value) => SyncPartsToFullProxy();
        partial void OnProxyPortChanged(string value) => SyncPartsToFullProxy();
        partial void OnProxyUsernameChanged(string value) => SyncPartsToFullProxy();
        partial void OnProxyPasswordChanged(string value) => SyncPartsToFullProxy();

        partial void OnProxyChangeLocationByIpChanged(bool value)
        {
            QueueConfigSave();
            SaveCommand.NotifyCanExecuteChanged();
        }

        partial void OnProxyChangeTimezoneByIpChanged(bool value)
        {
            QueueConfigSave();
            SaveCommand.NotifyCanExecuteChanged();
        }

        partial void OnSelectedProxyTypeChanged(string value)
        {
            if (!_isInitializing)
            {
                QueueConfigSave();
                SaveCommand.NotifyCanExecuteChanged();
            }
        }

        private void SyncPartsToFullProxy()
        {
            if (_isInitializing || _isSyncing)
                return;

            _isSyncing = true;

            RefreshFullProxyStringFromParts();

            _isSyncing = false;
            QueueConfigSave();
            SaveCommand.NotifyCanExecuteChanged();
        }

        public void ParseProxyString(string input)
        {
            ProxyHost = string.Empty;
            ProxyPort = string.Empty;
            ProxyUsername = string.Empty;
            ProxyPassword = string.Empty;

            if (string.IsNullOrWhiteSpace(input))
            {
                SaveCommand.NotifyCanExecuteChanged();
                return;
            }

            try
            {
                if (TryParseProxyInput(input, out var host, out var port, out var username, out var password))
                {
                    ProxyHost = host;
                    ProxyPort = port;
                    ProxyUsername = username;
                    ProxyPassword = password;
                }
            }
            catch (Exception)
            {
                _logger.LogWarning("Proxy input could not be parsed.");
            }

            SaveCommand.NotifyCanExecuteChanged();
        }

        public FakeProxyDialogResult? BuildResult()
        {
            if (CanSave() && int.TryParse(ProxyPort, out var port))
            {
                return new FakeProxyDialogResult(
                    ProxyHost.Trim(),
                    port,
                    ProxyUsername.Trim(),
                    ProxyPassword.Trim(),
                    "Socks 5",
                    ProxyChangeLocationByIp,
                    ProxyChangeTimezoneByIp);
            }
            return null;
        }

        private bool CanSave()
        {
            return !string.IsNullOrWhiteSpace(ProxyHost)
                && IsProxyPartsValid(ProxyHost, ProxyPort, ProxyUsername, ProxyPassword);
        }

        private void RefreshFullProxyStringFromParts()
        {
            FullProxyString = AreProxyPartsEmpty()
                ? string.Empty
                : FormatProxyDisplayString(ProxyHost, ProxyPort, ProxyUsername, ProxyPassword);
        }

        private bool AreProxyInputsEmpty()
        {
            return string.IsNullOrWhiteSpace(FullProxyString) && AreProxyPartsEmpty();
        }

        private bool AreProxyPartsEmpty()
        {
            return string.IsNullOrWhiteSpace(ProxyHost)
                && string.IsNullOrWhiteSpace(ProxyPort)
                && string.IsNullOrWhiteSpace(ProxyUsername)
                && string.IsNullOrWhiteSpace(ProxyPassword);
        }

        private static bool TryParseProxyInput(
            string input,
            out string host,
            out string port,
            out string username,
            out string password)
        {
            host = string.Empty;
            port = string.Empty;
            username = string.Empty;
            password = string.Empty;

            var normalized = input.Trim();
            if (normalized.Length == 0)
                return false;

            if (normalized.StartsWith(SocksScheme, StringComparison.OrdinalIgnoreCase))
            {
                normalized = normalized[SocksScheme.Length..];
            }
            else if (normalized.Contains(SchemeMarker, StringComparison.Ordinal))
            {
                return false;
            }

            if (normalized.Contains(AuthSeparator))
            {
                var proxyParts = normalized.Split(AuthSeparator, StringSplitOptions.TrimEntries);
                if (proxyParts.Length != 2)
                    return false;

                var authParts = proxyParts[0].Split(ProxySegmentSeparator, StringSplitOptions.TrimEntries);
                var endpointParts = proxyParts[1].Split(ProxySegmentSeparator, StringSplitOptions.TrimEntries);
                if (authParts.Length != 2 || endpointParts.Length != 2)
                    return false;

                username = authParts[0];
                password = authParts[1];
                host = endpointParts[0];
                port = endpointParts[1];
            }
            else
            {
                var proxyParts = normalized.Split(ProxySegmentSeparator, StringSplitOptions.TrimEntries);
                if (proxyParts.Length is not (2 or 4))
                    return false;

                host = proxyParts[0];
                port = proxyParts[1];
                username = proxyParts.Length == 4 ? proxyParts[2] : string.Empty;
                password = proxyParts.Length == 4 ? proxyParts[3] : string.Empty;
            }

            return IsProxyPartsValid(host, port, username, password);
        }

        private static string FormatProxyDisplayString(
            string host,
            string port,
            string username,
            string password)
        {
            var trimmedHost = host.Trim();
            var trimmedPort = port.Trim();
            var trimmedUsername = username.Trim();
            var trimmedPassword = password.Trim();

            return string.IsNullOrWhiteSpace(trimmedUsername) && string.IsNullOrWhiteSpace(trimmedPassword)
                ? $"{trimmedHost}:{trimmedPort}"
                : $"{trimmedHost}:{trimmedPort}:{trimmedUsername}:{trimmedPassword}";
        }

        private static bool IsProxyPartsValid(
            string host,
            string port,
            string username,
            string password)
        {
            if (string.IsNullOrWhiteSpace(host))
                return false;

            if (!int.TryParse(port, out var parsedPort)
                || parsedPort < MinProxyPort
                || parsedPort > MaxProxyPort)
            {
                return false;
            }

            return string.IsNullOrWhiteSpace(username) == string.IsNullOrWhiteSpace(password);
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
                _logger.LogError(exception, "Failed to save proxy settings.");
            }
        }

        private readonly record struct ProxyConfigSnapshot(
            string Serial,
            bool ShouldPersistProxy,
            string FullProxyString,
            string ProxyType,
            bool ChangeLocationByIp,
            bool ChangeTimezoneByIp);
    }
}
