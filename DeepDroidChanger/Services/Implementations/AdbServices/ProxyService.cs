using DeepDroidChanger.Models;
using DeepDroidChanger.Constants;
using DeepDroidChanger.Helpers;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http;
using Microsoft.Extensions.Logging;
using MihaZupan;

namespace DeepDroidChanger.Services
{
    public sealed class ProxyService : IProxyService
    {
        private const int OpenPackageDelayMilliseconds = 3000;
        private const int ProxyConnectDelayMilliseconds = 5000;
        private const int NetworkRetryDelayMilliseconds = 5000;
        private const int InternetCheckTimeoutSeconds = 90;
        private static readonly TimeSpan InternetCheckTimeout = TimeSpan.FromSeconds(InternetCheckTimeoutSeconds);
        private static readonly TimeSpan NetworkRetryDelay = TimeSpan.FromMilliseconds(NetworkRetryDelayMilliseconds);

        private readonly IAdbCommandService _adbCommandService;
        private readonly IRandomService _randomService;
        private readonly ILogger<ProxyService> _logger;
        private readonly Func<string, int, string, string, CancellationToken, Task<SocksProxyCheckResult?>> _proxyChecker;
        private readonly Func<string, SocksProxyCheckResult?, CancellationToken, Task<string>> _interfaceIpResolver;
        private readonly Func<TimeSpan, CancellationToken, Task> _delay;

        public ProxyService(
            IAdbCommandService adbCommandService,
            IRandomService randomService,
            ILogger<ProxyService> logger)
            : this(adbCommandService, randomService, logger, null, null, null)
        {
        }

        internal ProxyService(
            IAdbCommandService adbCommandService,
            IRandomService randomService,
            ILogger<ProxyService> logger,
            Func<string, int, string, string, CancellationToken, Task<SocksProxyCheckResult?>>? proxyChecker,
            Func<string, SocksProxyCheckResult?, CancellationToken, Task<string>>? interfaceIpResolver,
            Func<TimeSpan, CancellationToken, Task>? delay)
        {
            _adbCommandService = adbCommandService;
            _randomService = randomService;
            _logger = logger;
            _proxyChecker = proxyChecker ?? CheckSocksProxyAsync;
            _interfaceIpResolver = interfaceIpResolver ?? ResolveInterfaceIpAsync;
            _delay = delay ?? Task.Delay;
        }

        public async Task StartProxyAsync(
            string serial,
            string host,
            int port,
            string username,
            string password,
            string proxyType,
            CancellationToken cancellationToken)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(serial);
            ArgumentException.ThrowIfNullOrWhiteSpace(host);
            if (port is < 1 or > 65535)
                throw new ArgumentOutOfRangeException(nameof(port), port, "Proxy port must be between 1 and 65535.");
            if (string.IsNullOrWhiteSpace(username) != string.IsNullOrWhiteSpace(password))
                throw new ArgumentException("Proxy username and password must either both be provided or both be empty.");
            if (!string.Equals(proxyType, DeepProxyConstants.SocksProxyType, StringComparison.OrdinalIgnoreCase))
                throw new NotSupportedException("Fake Proxy currently supports SOCKS5 only.");

            var hasCredentials = !string.IsNullOrWhiteSpace(username) && !string.IsNullOrWhiteSpace(password);
            _logger.LogInformation(
                "Starting SOCKS5 fake proxy for device {Serial}. CredentialsPresent: {HasCredentials}",
                serial,
                hasCredentials);

            var proxyCheck = await _proxyChecker(host, port, username, password, cancellationToken).ConfigureAwait(false);
            var interfaceIp = await _interfaceIpResolver(host, proxyCheck, cancellationToken).ConfigureAwait(false);
            _logger.LogInformation("Selected an interface IP for device {Serial}.", serial);

            var wifiDisableAttempted = false;
            var proxySetupStarted = false;
            try
            {
                wifiDisableAttempted = true;
                await _adbCommandService.SetWifiAsync(serial, enabled: false, cancellationToken).ConfigureAwait(false);

                proxySetupStarted = true;
                await StopProxyAsync(serial, cancellationToken).ConfigureAwait(false);
                await SetDeepProxyPropertiesAsync(serial, host, port, username, password, interfaceIp, cancellationToken).ConfigureAwait(false);
                await StartDeepProxyServiceAsync(serial, cancellationToken).ConfigureAwait(false);

                await _delay(TimeSpan.FromMilliseconds(ProxyConnectDelayMilliseconds), cancellationToken).ConfigureAwait(false);
                await _adbCommandService.SetWifiAsync(serial, enabled: true, cancellationToken).ConfigureAwait(false);
                wifiDisableAttempted = false;
                await WaitForDeviceNetworkAsync(serial, cancellationToken).ConfigureAwait(false);
                await _adbCommandService.OpenLinkAsync(serial, DeepProxyConstants.BrowserLeaksUrl, cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                await RollbackFailedStartAsync(serial, proxySetupStarted, wifiDisableAttempted).ConfigureAwait(false);
                throw;
            }

            _logger.LogInformation("SOCKS5 fake proxy completed for device {Serial}.", serial);
        }

        private async Task RollbackFailedStartAsync(
            string serial,
            bool proxySetupStarted,
            bool wifiDisableAttempted)
        {
            _logger.LogWarning("Rolling back failed SOCKS5 proxy start for device {Serial}.", serial);

            if (proxySetupStarted)
            {
                await SafeExecuteAsync(
                        () => StopProxyAsync(serial, CancellationToken.None),
                        "rollback failed proxy setup")
                    .ConfigureAwait(false);
            }

            if (wifiDisableAttempted || proxySetupStarted)
            {
                await SafeExecuteAsync(
                        () => _adbCommandService.SetWifiAsync(serial, enabled: true, CancellationToken.None),
                        "restore Wi-Fi after failed proxy setup")
                    .ConfigureAwait(false);
            }
        }

        public async Task StopProxyAsync(string serial, CancellationToken cancellationToken)
        {
            _logger.LogInformation("Stopping SOCKS5 fake proxy on device {Serial}.", serial);

            foreach (var propertyName in new[]
                     {
                         DeepProxyConstants.ProxyIpProperty,
                         DeepProxyConstants.ProxyPortProperty,
                         DeepProxyConstants.ProxyUsernameProperty,
                         DeepProxyConstants.ProxyPasswordProperty,
                         DeepProxyConstants.InterfaceIpv4Property,
                         DeepProxyConstants.InterfaceIpv4PrefixProperty
                     })
            {
                await TrySetPropertyAsync(serial, propertyName, string.Empty, cancellationToken).ConfigureAwait(false);
            }

            await SafeExecuteAsync(() => _adbCommandService.ClearGlobalHttpProxyAsync(serial, cancellationToken), "clear global HTTP proxy").ConfigureAwait(false);

            await SafeExecuteAsync(async () =>
            {
                var result = await _adbCommandService.RunAdbShellAsync(
                    serial,
                    $"am start-foreground-service -n {DeepProxyConstants.ServiceComponent} -a {DeepProxyConstants.DisconnectAction}",
                    cancellationToken).ConfigureAwait(false);
                if (result.ExitCode != 0)
                {
                    throw new InvalidOperationException($"Failed to disconnect DeepProxy service: {GetCommandError(result)}");
                }
            }, "disconnect DeepProxy service").ConfigureAwait(false);

            await SafeExecuteAsync(() => _adbCommandService.ForceStopPackageAsync(serial, DeepProxyConstants.PackageName, cancellationToken), "force stop package").ConfigureAwait(false);
            await SafeExecuteAsync(() => _adbCommandService.ClearPackageAsync(serial, DeepProxyConstants.PackageName, cancellationToken), "clear package").ConfigureAwait(false);

            _logger.LogInformation("SOCKS5 fake proxy stopped on device {Serial}.", serial);
        }

        private async Task<SocksProxyCheckResult?> CheckSocksProxyAsync(
            string host,
            int port,
            string username,
            string password,
            CancellationToken cancellationToken)
        {
            var hasCredentials = !string.IsNullOrWhiteSpace(username) && !string.IsNullOrWhiteSpace(password);
            _logger.LogInformation(
                "Checking SOCKS5 proxy public IP. CredentialsPresent: {HasCredentials}",
                hasCredentials);

            try
            {
                var proxy = hasCredentials
                    ? new HttpToSocks5Proxy(host, port, username, password)
                    : new HttpToSocks5Proxy(host, port);

                using var handler = new HttpClientHandler { Proxy = proxy };
                using var client = new HttpClient(handler, disposeHandler: true)
                {
                    Timeout = TimeSpan.FromSeconds(IpGeolocationConstants.HttpTimeoutSeconds),
                };
                using var response = await client.GetAsync(IpGeolocationConstants.Endpoint, cancellationToken).ConfigureAwait(false);
                var content = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

                if (IsRateLimited(response, out bool retryAfterProvided))
                {
                    _logger.LogWarning(
                        "SOCKS5 proxy check was rate-limited. RetryAfterProvided: {RetryAfterProvided}. No automatic retry will be attempted.",
                        retryAfterProvided);
                    return null;
                }

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning(
                        "SOCKS5 proxy check returned status {StatusCode}.",
                        response.StatusCode);
                    return null;
                }

                if (!IpGeolocationResponseParser.TryParse(content, out IpGeolocationInfo info))
                    return null;

                _logger.LogInformation(
                    "SOCKS5 proxy check succeeded. CountryCodePresent: {CountryCodePresent}",
                    !string.IsNullOrWhiteSpace(info.CountryCode));

                return new SocksProxyCheckResult(info.PublicIp, info.CountryCode);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("SOCKS5 proxy check timed out.");
                return null;
            }
            catch (Exception exception)
            {
                _logger.LogWarning(
                    "SOCKS5 proxy check failed. FailureType: {FailureType}.",
                    exception.GetType().Name);
                return null;
            }
        }

        internal static bool IsRateLimited(HttpResponseMessage response, out bool retryAfterProvided)
        {
            ArgumentNullException.ThrowIfNull(response);
            retryAfterProvided = response.Headers.RetryAfter != null;
            return response.StatusCode == HttpStatusCode.TooManyRequests;
        }

        private async Task<string> ResolveInterfaceIpAsync(
            string proxyHost,
            SocksProxyCheckResult? proxyCheck,
            CancellationToken cancellationToken)
        {
            var fallbackIp = _randomService.GetRandomLocalIp();
            _logger.LogDebug("Generated a fallback local interface IP.");

            if (proxyCheck == null)
            {
                _logger.LogWarning(
                    "SOCKS5 proxy public IP check failed or returned no data. Using a random local interface IP.");
                return fallbackIp;
            }

            if (!string.IsNullOrWhiteSpace(proxyCheck.PublicIp)
                && !string.Equals(proxyCheck.PublicIp, proxyHost, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogInformation("Using the proxy public IP as interface IP.");
                return proxyCheck.PublicIp;
            }

            var hasPublicIp = !string.IsNullOrWhiteSpace(proxyCheck.PublicIp);
            if (!string.IsNullOrWhiteSpace(proxyCheck.CountryCode))
            {
                var countryInterfaceIp = await FetchRandomInterfaceIpAsync(proxyCheck.CountryCode, cancellationToken).ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(countryInterfaceIp))
                {
                    if (hasPublicIp)
                    {
                        _logger.LogWarning(
                            "SOCKS5 proxy public IP matches the proxy host. Using a random country CIDR interface IP for {CountryCode}.",
                            proxyCheck.CountryCode);
                    }
                    else
                    {
                        _logger.LogWarning(
                            "SOCKS5 proxy check returned country {CountryCode} but no public IP. Using a random country CIDR interface IP.",
                            proxyCheck.CountryCode);
                    }

                    return countryInterfaceIp;
                }

                if (hasPublicIp)
                {
                    _logger.LogWarning(
                        "SOCKS5 proxy public IP matches the proxy host, but random country CIDR lookup failed for {CountryCode}.",
                        proxyCheck.CountryCode);
                }
                else
                {
                    _logger.LogWarning(
                        "SOCKS5 proxy check returned country {CountryCode} but no public IP, and random country CIDR lookup failed.",
                        proxyCheck.CountryCode);
                }
            }

            if (string.IsNullOrWhiteSpace(proxyCheck.PublicIp))
            {
                _logger.LogWarning(
                    "SOCKS5 proxy check did not return a public IP. Using a random local interface IP.");
                return fallbackIp;
            }

            _logger.LogWarning(
                "SOCKS5 proxy public IP matches the proxy host. Using a random local interface IP.");
            return fallbackIp;
        }

        private async Task<string?> FetchRandomInterfaceIpAsync(string countryCode, CancellationToken cancellationToken)
        {
            var normalizedCountryCode = countryCode.Trim().ToLowerInvariant();
            var url = string.Format(CultureInfo.InvariantCulture, DeepProxyConstants.CountryIpv4BlocksUrlFormat, normalizedCountryCode);

            try
            {
                _logger.LogDebug("Fetching country CIDR list for {CountryCode}.", normalizedCountryCode);
                using var client = new HttpClient
                {
                    Timeout = TimeSpan.FromSeconds(DeepProxyConstants.RemoteRequestTimeoutSeconds),
                };
                var data = await client.GetStringAsync(url, cancellationToken).ConfigureAwait(false);
                var cidrs = data
                    .Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(s => s.Trim())
                    .Where(s => s.Contains('/', StringComparison.Ordinal))
                    .ToArray();

                if (cidrs.Length == 0)
                {
                    _logger.LogWarning("Country CIDR list for {CountryCode} was empty.", countryCode);
                    return null;
                }

                return _randomService.PickRandom(cidrs);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("Country CIDR lookup timed out for {CountryCode}.", countryCode);
                return null;
            }
            catch (Exception exception)
            {
                _logger.LogWarning(
                    "Failed to fetch random interface IP for country {CountryCode}. FailureType: {FailureType}.",
                    countryCode,
                    exception.GetType().Name);
                return null;
            }
        }

        private async Task SetDeepProxyPropertiesAsync(
            string serial,
            string host,
            int port,
            string username,
            string password,
            string interfaceIp,
            CancellationToken cancellationToken)
        {
            _logger.LogInformation("Setting DeepProxy properties for device {Serial}.", serial);
            await EnsureDeepDroidDeviceAsync(serial, cancellationToken).ConfigureAwait(false);

            await _adbCommandService.SetPropertyAsync(serial, DeepProxyConstants.ProxyIpProperty, host, cancellationToken).ConfigureAwait(false);
            await _adbCommandService.SetPropertyAsync(serial, DeepProxyConstants.ProxyPortProperty, port.ToString(CultureInfo.InvariantCulture), cancellationToken).ConfigureAwait(false);
            await _adbCommandService.SetPropertyAsync(serial, DeepProxyConstants.ProxyUsernameProperty, username ?? string.Empty, cancellationToken).ConfigureAwait(false);
            await _adbCommandService.SetPropertyAsync(serial, DeepProxyConstants.ProxyPasswordProperty, password ?? string.Empty, cancellationToken).ConfigureAwait(false);

            var interfaceIpParts = interfaceIp.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            await _adbCommandService.SetPropertyAsync(serial, DeepProxyConstants.InterfaceIpv4Property, interfaceIpParts[0], cancellationToken).ConfigureAwait(false);
            await _adbCommandService.SetPropertyAsync(
                serial,
                DeepProxyConstants.InterfaceIpv4PrefixProperty,
                interfaceIpParts.Length == 2 ? interfaceIpParts[1] : string.Empty,
                cancellationToken).ConfigureAwait(false);
        }

        private async Task StartDeepProxyServiceAsync(string serial, CancellationToken cancellationToken)
        {
            _logger.LogInformation("Opening DeepProxy package on device {Serial}.", serial);
            await _adbCommandService.OpenPackageAsync(serial, DeepProxyConstants.PackageName, cancellationToken).ConfigureAwait(false);
            await _delay(TimeSpan.FromMilliseconds(OpenPackageDelayMilliseconds), cancellationToken).ConfigureAwait(false);

            var resultAppOps = await _adbCommandService.RunAdbShellAsync(
                serial,
                $"cmd appops set {DeepProxyConstants.PackageName} {DeepProxyConstants.EstablishVpnServiceAppOp} allow",
                cancellationToken).ConfigureAwait(false);
            if (resultAppOps.ExitCode != 0)
            {
                throw new InvalidOperationException($"ADB shell operation 'allow DeepProxy VPN app op' failed on device {serial}: {GetCommandError(resultAppOps)}");
            }

            var resultVpn = await _adbCommandService.RunAdbShellAsync(
                serial,
                $"am start-foreground-service -n {DeepProxyConstants.ServiceComponent} -a {DeepProxyConstants.ConnectAction}",
                cancellationToken).ConfigureAwait(false);
            if (resultVpn.ExitCode != 0)
            {
                throw new InvalidOperationException($"ADB shell operation 'connect DeepProxy service' failed on device {serial}: {GetCommandError(resultVpn)}");
            }

            var resultMainActivity = await _adbCommandService.RunAdbShellAsync(
                serial,
                $"am start-activity -n {DeepProxyConstants.ServiceMainActivity}",
                cancellationToken).ConfigureAwait(false);
            if (resultMainActivity.ExitCode != 0)
            {
                throw new InvalidOperationException($"ADB shell operation 'start DeepProxy main activity' failed on device {serial}: {GetCommandError(resultMainActivity)}");
            }
        }

        private async Task WaitForDeviceNetworkAsync(string serial, CancellationToken cancellationToken)
        {
            var stopwatch = Stopwatch.StartNew();
            var attempt = 1;

            while (stopwatch.Elapsed < InternetCheckTimeout)
            {
                var publicIp = await _adbCommandService.CurlAsync(serial, DeepProxyConstants.PublicIpCheckUrl, cancellationToken).ConfigureAwait(false);
                _logger.LogInformation(
                    "Device network check attempt {Attempt} for {Serial}. ElapsedSeconds: {ElapsedSeconds}. PublicIpPresent: {PublicIpPresent}",
                    attempt,
                    serial,
                    (int)stopwatch.Elapsed.TotalSeconds,
                    !string.IsNullOrWhiteSpace(publicIp));

                if (!string.IsNullOrWhiteSpace(publicIp))
                {
                    _logger.LogInformation(
                        "Device internet check succeeded for {Serial} after {ElapsedSeconds} seconds.",
                        serial,
                        (int)stopwatch.Elapsed.TotalSeconds);
                    return;
                }

                await _adbCommandService.SetWifiAsync(serial, enabled: true, cancellationToken).ConfigureAwait(false);
                await _adbCommandService.OpenWifiSettingsAsync(serial, cancellationToken).ConfigureAwait(false);

                var remaining = InternetCheckTimeout - stopwatch.Elapsed;
                if (remaining <= TimeSpan.Zero)
                    break;

                await _delay(remaining < NetworkRetryDelay ? remaining : NetworkRetryDelay, cancellationToken).ConfigureAwait(false);
                attempt++;
            }

            _logger.LogError(
                "Device internet check failed for {Serial} after {TimeoutSeconds} seconds. Please check the proxy.",
                serial,
                InternetCheckTimeoutSeconds);
            throw new TimeoutException($"Internet check failed after {InternetCheckTimeoutSeconds} seconds. Please check the proxy.");
        }

        private async Task EnsureDeepDroidDeviceAsync(string serial, CancellationToken cancellationToken)
        {
            var marker = await _adbCommandService.GetPropertyAsync(serial, PropertyConstants.Prop_DeepDroidDevice, cancellationToken).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(marker))
                return;

            _logger.LogError("Device {Serial} is not a DeepDroid device. Property {PropertyName} was empty.", serial, PropertyConstants.Prop_DeepDroidDevice);
            throw new InvalidOperationException($"Device {serial} is not a DeepDroid device.");
        }

        private async Task TrySetPropertyAsync(
            string serial,
            string propertyName,
            string value,
            CancellationToken cancellationToken)
        {
            try
            {
                await _adbCommandService.SetPropertyAsync(serial, propertyName, value, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                _logger.LogWarning(exception, "Failed to set cleanup property {PropertyName} for device {Serial}.", propertyName, serial);
            }
        }

        private static string GetCommandError(CommandResult result)
        {
            return $"exit code {result.ExitCode}";
        }

        private async Task SafeExecuteAsync(Func<Task> action, string purpose)
        {
            try
            {
                await action().ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                _logger.LogWarning(exception, "Cleanup step failed. Purpose: {Purpose}.", purpose);
            }
        }
    }
}
