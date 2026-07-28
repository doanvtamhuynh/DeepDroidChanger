using DeepDroidChanger.Models;
using System.IO;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace DeepDroidChanger.Services
{
    public sealed class PackageInstallService : IPackageInstallService
    {
        private const string EmptyOption = "";
        private const string GrantPermissionsArgument = "-g ";
        private const string ShellQuote = "'";
        private const string EscapedShellQuote = "'\\''";
        private const string DoubleQuote = "\"";
        private const string EscapedDoubleQuote = "\\\"";

        private readonly IAdbCommandService _commandService;
        private readonly IXapkPackageService _xapkPackageService;
        private readonly ILogger<PackageInstallService> _logger;

        public PackageInstallService(
            IAdbCommandService commandService,
            IXapkPackageService xapkPackageService,
            ILogger<PackageInstallService> logger)
        {
            _commandService = commandService;
            _xapkPackageService = xapkPackageService;
            _logger = logger;
        }

        public async Task<InstallPackageResult> InstallAsync(
            string serial,
            string filePath,
            InstallPackageOptions options,
            CancellationToken cancellationToken)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(serial);
            ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
            ArgumentNullException.ThrowIfNull(options);

            cancellationToken.ThrowIfCancellationRequested();

            if (!File.Exists(filePath))
                return CreateFailure(filePath, "Log_InstallPackageFileMissing");

            var extension = Path.GetExtension(filePath);
            if (string.Equals(extension, ".apk", StringComparison.OrdinalIgnoreCase))
                return await InstallApkAsync(serial, filePath, options, cancellationToken).ConfigureAwait(false);

            if (string.Equals(extension, ".xapk", StringComparison.OrdinalIgnoreCase))
                return await InstallXapkAsync(serial, filePath, options, cancellationToken).ConfigureAwait(false);

            return CreateFailure(filePath, "Log_InstallPackageUnsupportedFile");
        }

        private async Task<InstallPackageResult> InstallApkAsync(
            string serial,
            string apkPath,
            InstallPackageOptions options,
            CancellationToken cancellationToken)
        {
            var arguments = CreateInstallArguments(apkPath, options);
            var result = await _commandService.RunAdbAsync(serial, arguments, cancellationToken).ConfigureAwait(false);
            return ParseInstallResult(apkPath, result);
        }

        private async Task<InstallPackageResult> InstallXapkAsync(
            string serial,
            string xapkPath,
            InstallPackageOptions options,
            CancellationToken cancellationToken)
        {
            var tempDirectory = CreateTempInstallDirectory();

            try
            {
                var packageInfo = await _xapkPackageService
                    .ExtractAsync(xapkPath, tempDirectory, cancellationToken)
                    .ConfigureAwait(false);

                var apkPaths = packageInfo.ApkFilePaths;
                var installResult = apkPaths.Count == 1
                    ? await InstallApkAsync(serial, apkPaths[0], options, cancellationToken).ConfigureAwait(false)
                    : await InstallMultipleAsync(serial, xapkPath, apkPaths, options, cancellationToken).ConfigureAwait(false);

                if (!installResult.Success)
                    return new InstallPackageResult(
                        xapkPath,
                        false,
                        installResult.MessageResourceKey,
                        installResult.FailureCode,
                        installResult.MessageArguments.ToArray());

                var obbPushed = await PushObbFilesAsync(serial, packageInfo, cancellationToken).ConfigureAwait(false);
                if (!obbPushed)
                    return CreateFailure(xapkPath, "Log_InstallPackageObbPushFailed");

                return CreateSuccess(xapkPath);
            }
            catch (InvalidDataException)
            {
                _logger.LogWarning("Invalid XAPK package.");
                return CreateFailure(xapkPath, "Log_InstallPackageInvalidXapk");
            }
            catch (JsonException)
            {
                _logger.LogWarning("Invalid XAPK manifest.");
                return CreateFailure(xapkPath, "Log_InstallPackageInvalidXapk");
            }
            catch (IOException)
            {
                _logger.LogWarning("Failed to read or extract package file.");
                return CreateFailure(xapkPath, "Log_InstallPackageInvalidXapk");
            }
            catch (UnauthorizedAccessException)
            {
                _logger.LogWarning("Package file access denied.");
                return CreateFailure(xapkPath, "Log_InstallPackageInvalidXapk");
            }
            finally
            {
                DeleteTempDirectory(tempDirectory);
            }
        }

        private async Task<InstallPackageResult> InstallMultipleAsync(
            string serial,
            string displayFilePath,
            IReadOnlyList<string> apkPaths,
            InstallPackageOptions options,
            CancellationToken cancellationToken)
        {
            var installOptions = CreateInstallOptions(options);
            var quotedApkPaths = string.Join(" ", apkPaths.Select(QuoteProcessArgument));
            var arguments = string.Format(
                "install-multiple -r {0}",
                $"{installOptions}{quotedApkPaths}");

            var result = await _commandService.RunAdbAsync(serial, arguments, cancellationToken).ConfigureAwait(false);
            return ParseInstallResult(displayFilePath, result);
        }

        private async Task<bool> PushObbFilesAsync(
            string serial,
            XapkPackageInfo packageInfo,
            CancellationToken cancellationToken)
        {
            if (packageInfo.ObbFiles.Count == 0)
                return true;

            var remoteDirectory = string.Format("/sdcard/Android/obb/{0}", packageInfo.PackageName);
            var mkdirCommand = string.Format("mkdir -p {0}", QuoteShellValue(remoteDirectory));
            var mkdirResult = await _commandService.RunAdbShellAsync(serial, mkdirCommand, cancellationToken).ConfigureAwait(false);
            if (mkdirResult.ExitCode != 0)
                return false;

            foreach (var obbFile in packageInfo.ObbFiles)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var remotePath = $"{remoteDirectory}/{obbFile.FileName}";
                var pushArguments = string.Format(
                    "push {0} {1}",
                    QuoteProcessArgument(obbFile.LocalPath),
                    QuoteProcessArgument(remotePath));
                var pushResult = await _commandService.RunAdbAsync(serial, pushArguments, cancellationToken).ConfigureAwait(false);
                if (pushResult.ExitCode != 0)
                    return false;
            }

            return true;
        }

        private static string CreateInstallArguments(string apkPath, InstallPackageOptions options)
        {
            return string.Format(
                "install -r {0}{1}",
                CreateInstallOptions(options),
                QuoteProcessArgument(apkPath));
        }

        private static string CreateInstallOptions(InstallPackageOptions options)
        {
            var grantPermissionsArgument = options.GrantPermissions ? GrantPermissionsArgument : EmptyOption;
            var allowDowngradeArgument = options.AllowDowngrade ? "-d " : EmptyOption;
            return $"{grantPermissionsArgument}{allowDowngradeArgument}";
        }

        private static InstallPackageResult ParseInstallResult(string filePath, CommandResult result)
        {
            var output = $"{result.StandardOutput}\n{result.StandardError}";
            if (output.Contains("Success", StringComparison.OrdinalIgnoreCase))
                return CreateSuccess(filePath);

            var failureCode = ExtractFailureCode(output);
            if (!string.IsNullOrWhiteSpace(failureCode))
                return CreateAdbFailure(filePath, failureCode);

            return result.ExitCode == 0
                ? CreateSuccess(filePath)
                : CreateFailure(filePath, "Log_InstallPackageAdbFailure");
        }

        private static string ExtractFailureCode(string output)
        {
            var start = output.IndexOf("Failure [", StringComparison.OrdinalIgnoreCase);
            if (start < 0)
                return string.Empty;

            start += "Failure [".Length;
            var end = output.IndexOf("]", start, StringComparison.OrdinalIgnoreCase);
            if (end <= start)
                return string.Empty;

            return output[start..end].Trim();
        }

        private static InstallPackageResult CreateAdbFailure(string filePath, string failureCode)
        {
            var failureCategory = ExtractFailureCategory(failureCode);
            return failureCategory switch
            {
                "INSTALL_FAILED_ALREADY_EXISTS" => CreateFailure(
                    filePath,
                    "Log_InstallPackageAlreadyExists",
                    failureCode),
                "INSTALL_FAILED_VERSION_DOWNGRADE" => CreateFailure(
                    filePath,
                    "Log_InstallPackageVersionDowngrade",
                    failureCode),
                "INSTALL_FAILED_INSUFFICIENT_STORAGE" => CreateFailure(
                    filePath,
                    "Log_InstallPackageInsufficientStorage",
                    failureCode),
                "INSTALL_FAILED_INVALID_APK" => CreateFailure(
                    filePath,
                    "Log_InstallPackageInvalidApk",
                    failureCode),
                "INSTALL_FAILED_NO_MATCHING_ABIS" => CreateFailure(
                    filePath,
                    "Log_InstallPackageNoMatchingAbis",
                    failureCode),
                "INSTALL_FAILED_MISSING_SHARED_LIBRARY" => CreateFailure(
                    filePath,
                    "Log_InstallPackageMissingSharedLibrary",
                    failureCode),
                _ => CreateFailure(
                    filePath,
                    "Log_InstallPackageAdbFailureCodeFormat",
                    failureCode,
                    failureCode)
            };
        }

        private static string ExtractFailureCategory(string failureCode)
        {
            var detailSeparatorIndex = failureCode.IndexOf(':');
            return detailSeparatorIndex <= 0
                ? failureCode.Trim()
                : failureCode[..detailSeparatorIndex].Trim();
        }

        private static InstallPackageResult CreateSuccess(string filePath)
        {
            return new InstallPackageResult(filePath, true, "Log_InstallPackageSuccess");
        }

        private static InstallPackageResult CreateFailure(
            string filePath,
            string messageResourceKey,
            string? failureCode = null,
            params object[] messageArguments)
        {
            return new InstallPackageResult(
                filePath,
                false,
                messageResourceKey,
                failureCode,
                messageArguments);
        }

        private static string CreateTempInstallDirectory()
        {
            return Path.Combine(
                Path.GetTempPath(),
                "DeepDroidChanger",
                "Install",
                Guid.NewGuid().ToString("N"));
        }

        private void DeleteTempDirectory(string tempDirectory)
        {
            try
            {
                if (Directory.Exists(tempDirectory))
                    Directory.Delete(tempDirectory, recursive: true);
            }
            catch (IOException exception)
            {
                _logger.LogDebug(exception, "Failed to delete package install temp directory.");
            }
            catch (UnauthorizedAccessException exception)
            {
                _logger.LogDebug(exception, "Access denied while deleting package install temp directory.");
            }
        }

        private static string QuoteProcessArgument(string value)
        {
            return $"{DoubleQuote}{value.Replace(DoubleQuote, EscapedDoubleQuote, StringComparison.Ordinal)}{DoubleQuote}";
        }

        private static string QuoteShellValue(string value)
        {
            return $"{ShellQuote}{value.Replace(ShellQuote, EscapedShellQuote, StringComparison.Ordinal)}{ShellQuote}";
        }
    }
}
