using DeepDroidChanger.Models;
using DeepDroidChanger.Constants;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;
using Microsoft.Extensions.Logging;

namespace DeepDroidChanger.Services
{
    public sealed class DeviceIntegrityService : IDeviceIntegrityService
    {
        private static readonly HttpClient HttpClientInstance = new()
        {
            Timeout = TimeSpan.FromSeconds(30),
        };
        private readonly IAdbCommandService _adbCommandService;
        private readonly IRandomService _randomService;
        private readonly ILogger<DeviceIntegrityService> _logger;
        private readonly Func<string, int, CancellationToken, Task<string>> _downloadString;

        public DeviceIntegrityService(
            IAdbCommandService adbCommandService,
            IRandomService randomService,
            ILogger<DeviceIntegrityService> logger)
            : this(adbCommandService, randomService, logger, null)
        {
        }

        internal DeviceIntegrityService(
            IAdbCommandService adbCommandService,
            IRandomService randomService,
            ILogger<DeviceIntegrityService> logger,
            Func<string, int, CancellationToken, Task<string>>? downloadString)
        {
            _adbCommandService = adbCommandService;
            _randomService = randomService;
            _logger = logger;
            _downloadString = downloadString ?? DownloadStringBoundedAsync;
        }

        public async Task<string?> TryGetRandomSecurityPatchAsync(CancellationToken cancellationToken)
        {
            try
            {
                string pifJson = await _downloadString(
                        UrlConstants.Pif,
                        2 * 1024 * 1024,
                        cancellationToken)
                    .ConfigureAwait(false);
                EnsureContentSize(pifJson, 2 * 1024 * 1024, "PIF JSON");

                IReadOnlyList<Integrity> candidates = ParsePifJson(pifJson)?
                    .Where(item => !string.IsNullOrWhiteSpace(item.SECURITY_PATCH))
                    .ToArray()
                    ?? Array.Empty<Integrity>();
                if (candidates.Count == 0)
                {
                    _logger.LogWarning("Integrity security patch download contained no usable records.");
                    return null;
                }

                return _randomService.PickRandom(candidates).SECURITY_PATCH!.Trim();
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                _logger.LogWarning(exception, "Unable to load Integrity security patch; the random-device value will be used.");
                return null;
            }
        }

        public async Task UpdateIntegrityAsync(string serial, bool fromServer, string? jsonPath, CancellationToken cancellationToken)
        {
            _logger.LogInformation(
                "Updating Integrity/PIF for device {Serial}. FromServer: {FromServer}, LocalFileSelected: {LocalFileSelected}.",
                serial,
                fromServer,
                !string.IsNullOrWhiteSpace(jsonPath));

            string pifJson;
            if (fromServer)
            {
                pifJson = await _downloadString(
                        UrlConstants.Pif,
                        2 * 1024 * 1024,
                        cancellationToken)
                    .ConfigureAwait(false);
                EnsureContentSize(pifJson, 2 * 1024 * 1024, "PIF JSON");
            }
            else
            {
                if (string.IsNullOrWhiteSpace(jsonPath) || !File.Exists(jsonPath))
                {
                    throw new FileNotFoundException("The selected PIF JSON file was not found.");
                }
                pifJson = await ReadLocalTextAsync(
                        jsonPath,
                        2 * 1024 * 1024,
                        "PIF JSON",
                        cancellationToken)
                    .ConfigureAwait(false);
            }

            var pifList = ParsePifJson(pifJson);
            if (pifList == null || pifList.Count == 0)
            {
                throw new InvalidOperationException("No valid PIF record found in JSON.");
            }

            var pifData = _randomService.PickRandom(pifList);

            ValidatePifData(pifData);

            string fingerprint = pifData.FINGERPRINT!;
            string[] parts = fingerprint.Split('/');
            List<string> splitFingerprint = new List<string>();
            foreach (string part in parts)
            {
                string[] subParts = part.Split(':');
                splitFingerprint.AddRange(subParts);
            }

            if (splitFingerprint.Count != 8)
            {
                throw new InvalidOperationException(
                    $"Fingerprint split failed. Expected 8 parts, but got {splitFingerprint.Count} parts.");
            }

            var releaseVersion = string.IsNullOrEmpty(pifData.RELEASE) ? splitFingerprint[3] : pifData.RELEASE;

            await _adbCommandService.SetPropertyAsync(serial, PropertyConstants.Integrity.Type, "user", cancellationToken).ConfigureAwait(false);
            await _adbCommandService.SetPropertyAsync(serial, PropertyConstants.Integrity.Tags, "release-keys", cancellationToken).ConfigureAwait(false);
            await _adbCommandService.SetPropertyAsync(serial, PropertyConstants.Integrity.Brand, splitFingerprint[0], cancellationToken).ConfigureAwait(false);
            await _adbCommandService.SetPropertyAsync(serial, PropertyConstants.Integrity.Product, splitFingerprint[1], cancellationToken).ConfigureAwait(false);
            await _adbCommandService.SetPropertyAsync(serial, PropertyConstants.Integrity.Device, splitFingerprint[2], cancellationToken).ConfigureAwait(false);
            await _adbCommandService.SetPropertyAsync(serial, PropertyConstants.Integrity.Board, splitFingerprint[2], cancellationToken).ConfigureAwait(false);
            await _adbCommandService.SetPropertyAsync(serial, PropertyConstants.Integrity.Hardware, splitFingerprint[2], cancellationToken).ConfigureAwait(false);
            await _adbCommandService.SetPropertyAsync(serial, PropertyConstants.Integrity.Id, splitFingerprint[4], cancellationToken).ConfigureAwait(false);
            await _adbCommandService.SetPropertyAsync(serial, PropertyConstants.Integrity.Incremental, splitFingerprint[5], cancellationToken).ConfigureAwait(false);
            await _adbCommandService.SetPropertyAsync(serial, PropertyConstants.Integrity.Fingerprint, fingerprint, cancellationToken).ConfigureAwait(false);
            await _adbCommandService.SetPropertyAsync(serial, PropertyConstants.Integrity.Manufacturer, pifData.MANUFACTURER ?? "Google", cancellationToken).ConfigureAwait(false);
            await _adbCommandService.SetPropertyAsync(serial, PropertyConstants.Integrity.Model, pifData.MODEL ?? "Pixel", cancellationToken).ConfigureAwait(false);
            await _adbCommandService.SetPropertyAsync(serial, PropertyConstants.Integrity.SecurityPatch, pifData.SECURITY_PATCH!, cancellationToken).ConfigureAwait(false);
            await _adbCommandService.SetPropertyAsync(serial, PropertyConstants.Integrity.DeviceInitialSdkInt, pifData.DEVICE_INITIAL_SDK_INT ?? "21", cancellationToken).ConfigureAwait(false);
            await _adbCommandService.SetPropertyAsync(serial, PropertyConstants.Integrity.SdkInt, pifData.SDK_INT ?? "32", cancellationToken).ConfigureAwait(false);
            await _adbCommandService.SetPropertyAsync(serial, PropertyConstants.Integrity.Release, releaseVersion, cancellationToken).ConfigureAwait(false);

            _logger.LogInformation("Successfully updated Integrity/PIF settings for device {Serial}.", serial);
        }

        public async Task UpdateKeyboxAsync(string serial, bool fromServer, string? keyboxPath, CancellationToken cancellationToken)
        {
            _logger.LogInformation(
                "Updating Keybox for device {Serial}. FromServer: {FromServer}, LocalFileSelected: {LocalFileSelected}.",
                serial,
                fromServer,
                !string.IsNullOrWhiteSpace(keyboxPath));

            string localPath;
            bool isTemporary = false;

            if (fromServer)
            {
                var xmlContent = await _downloadString(
                        UrlConstants.Keybox,
                        1024 * 1024,
                        cancellationToken)
                    .ConfigureAwait(false);
                ValidateKeyboxXml(xmlContent);

                localPath = Path.GetTempFileName();
                isTemporary = true;
                await File.WriteAllTextAsync(localPath, xmlContent, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                if (string.IsNullOrWhiteSpace(keyboxPath) || !File.Exists(keyboxPath))
                {
                    throw new FileNotFoundException("The selected Keybox file was not found.");
                }
                string xmlContent = await ReadLocalTextAsync(
                        keyboxPath,
                        1024 * 1024,
                        "Keybox XML",
                        cancellationToken)
                    .ConfigureAwait(false);
                ValidateKeyboxXml(xmlContent);
                localPath = keyboxPath;
            }

            try
            {
                var pushResult = await _adbCommandService.RunAdbAsync(serial, $"push \"{localPath}\" \"/data/local/tmp/keybox.xml\"", cancellationToken).ConfigureAwait(false);
                if (pushResult.ExitCode != 0)
                {
                    throw new InvalidOperationException(
                        $"ADB push keybox.xml failed with exit code {pushResult.ExitCode}.");
                }
                _logger.LogInformation("Successfully pushed keybox.xml to /data/local/tmp/ on device {Serial}.", serial);
            }
            finally
            {
                if (isTemporary && File.Exists(localPath))
                {
                    try
                    {
                        File.Delete(localPath);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to delete temporary Keybox file.");
                    }
                }
            }
        }

        public async Task ApplyAsync(
            string serial,
            UpdateIntegrityDialogResult result,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(result);

            if (result.UpdateIntegrityEnabled)
            {
                await UpdateIntegrityAsync(
                        serial,
                        result.UpdateIntegrityFromServer,
                        result.UpdateIntegrityFile,
                        cancellationToken)
                    .ConfigureAwait(false);
            }

            if (result.UpdateKeyboxEnabled)
            {
                await UpdateKeyboxAsync(
                        serial,
                        result.UpdateIntegrityFromServer,
                        result.UpdateKeyboxFile,
                        cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        private static List<Integrity>? ParsePifJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return null;

            var trimmed = json.Trim();
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

            if (trimmed.StartsWith('['))
            {
                return JsonSerializer.Deserialize<List<Integrity>>(trimmed, options);
            }
            else if (trimmed.StartsWith('{'))
            {
                var single = JsonSerializer.Deserialize<Integrity>(trimmed, options);
                return single != null ? new List<Integrity> { single } : null;
            }

            return null;
        }

        private static void ValidatePifData(Integrity pifData)
        {
            if (string.IsNullOrWhiteSpace(pifData.FINGERPRINT))
                throw new InvalidOperationException("PIF FINGERPRINT is missing or empty.");
            if (string.IsNullOrWhiteSpace(pifData.SECURITY_PATCH))
                throw new InvalidOperationException("PIF SECURITY_PATCH is missing or empty.");
        }

        private static void ValidateKeyboxXml(string xmlContent)
        {
            EnsureContentSize(xmlContent, 1024 * 1024, "Keybox XML");
            if (string.IsNullOrWhiteSpace(xmlContent))
                throw new InvalidOperationException("Keybox XML is empty.");

            var settings = new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                MaxCharactersInDocument = 1024 * 1024
            };

            try
            {
                using var stringReader = new StringReader(xmlContent);
                using XmlReader xmlReader = XmlReader.Create(stringReader, settings);
                XDocument document = XDocument.Load(xmlReader, LoadOptions.None);
                XElement? root = document.Root;
                bool hasKeybox = root != null
                    && string.Equals(root.Name.LocalName, "AndroidAttestation", StringComparison.Ordinal)
                    && root.Descendants().Any(element =>
                        string.Equals(element.Name.LocalName, "Keybox", StringComparison.Ordinal));
                if (!hasKeybox)
                    throw new InvalidOperationException("Keybox XML does not contain the required Android attestation structure.");
            }
            catch (XmlException exception)
            {
                throw new InvalidOperationException("Keybox XML is invalid or unsafe.", exception);
            }
        }

        private static void EnsureFileSize(string path, int maximumBytes, string contentName)
        {
            if (new FileInfo(path).Length > maximumBytes)
                throw new InvalidOperationException($"{contentName} exceeds the allowed size.");
        }

        private static void EnsureContentSize(string content, int maximumBytes, string contentName)
        {
            if (Encoding.UTF8.GetByteCount(content ?? string.Empty) > maximumBytes)
                throw new InvalidOperationException($"{contentName} exceeds the allowed size.");
        }

        private static async Task<string> DownloadStringBoundedAsync(
            string url,
            int maximumBytes,
            CancellationToken cancellationToken)
        {
            try
            {
                using HttpResponseMessage response = await HttpClientInstance
                    .GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                    .ConfigureAwait(false);
                response.EnsureSuccessStatusCode();
                return await ReadBoundedContentAsync(response.Content, maximumBytes, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new InvalidOperationException("Integrity data download timed out.");
            }
            catch (HttpRequestException)
            {
                throw new InvalidOperationException("Integrity data download failed.");
            }
        }

        internal static async Task<string> ReadBoundedContentAsync(
            HttpContent content,
            int maximumBytes,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(content);
            if (maximumBytes <= 0)
                throw new ArgumentOutOfRangeException(nameof(maximumBytes));
            if (content.Headers.ContentLength > maximumBytes)
                throw new InvalidOperationException("Downloaded content exceeds the allowed size.");

            await using Stream source = await content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using var destination = new MemoryStream(Math.Min(maximumBytes, 81920));
            byte[] buffer = new byte[81920];
            int totalBytes = 0;
            while (true)
            {
                int remainingProbeBytes = maximumBytes - totalBytes + 1;
                int read = await source
                    .ReadAsync(buffer.AsMemory(0, Math.Min(buffer.Length, remainingProbeBytes)), cancellationToken)
                    .ConfigureAwait(false);
                if (read == 0)
                    break;

                totalBytes += read;
                if (totalBytes > maximumBytes)
                    throw new InvalidOperationException("Downloaded content exceeds the allowed size.");

                await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            }

            destination.Position = 0;
            using var reader = new StreamReader(
                destination,
                Encoding.UTF8,
                detectEncodingFromByteOrderMarks: true,
                leaveOpen: false);
            return await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        }

        private static async Task<string> ReadLocalTextAsync(
            string path,
            int maximumBytes,
            string contentName,
            CancellationToken cancellationToken)
        {
            EnsureFileSize(path, maximumBytes, contentName);
            try
            {
                return await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                throw new InvalidOperationException($"{contentName} could not be read.");
            }
        }
    }
}
