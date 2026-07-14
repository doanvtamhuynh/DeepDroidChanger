using DeepDroidChanger.Models;
using DeepDroidChanger.Constants;
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
                return CreateFailure(filePath, DeviceLogResourceKeys.InstallPackageFileMissing);

            var extension = Path.GetExtension(filePath);
            if (string.Equals(extension, AdbInstallConstants.ApkExtension, StringComparison.OrdinalIgnoreCase))
                return await InstallApkAsync(serial, filePath, options, cancellationToken).ConfigureAwait(false);

            if (string.Equals(extension, AdbInstallConstants.XapkExtension, StringComparison.OrdinalIgnoreCase))
                return await InstallXapkAsync(serial, filePath, options, cancellationToken).ConfigureAwait(false);

            return CreateFailure(filePath, DeviceLogResourceKeys.InstallPackageUnsupportedFile);
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
                    return CreateFailure(xapkPath, DeviceLogResourceKeys.InstallPackageObbPushFailed);

                return CreateSuccess(xapkPath);
            }
            catch (InvalidDataException)
            {
                _logger.LogWarning("Invalid XAPK package.");
                return CreateFailure(xapkPath, DeviceLogResourceKeys.InstallPackageInvalidXapk);
            }
            catch (JsonException)
            {
                _logger.LogWarning("Invalid XAPK manifest.");
                return CreateFailure(xapkPath, DeviceLogResourceKeys.InstallPackageInvalidXapk);
            }
            catch (IOException)
            {
                _logger.LogWarning("Failed to read or extract package file.");
                return CreateFailure(xapkPath, DeviceLogResourceKeys.InstallPackageInvalidXapk);
            }
            catch (UnauthorizedAccessException)
            {
                _logger.LogWarning("Package file access denied.");
                return CreateFailure(xapkPath, DeviceLogResourceKeys.InstallPackageInvalidXapk);
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
                AdbInstallConstants.InstallMultipleArgumentsFormat,
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

            var remoteDirectory = string.Format(AdbInstallConstants.AndroidObbRemoteDirectoryFormat, packageInfo.PackageName);
            var mkdirCommand = string.Format(AdbInstallConstants.MakeDirectoryCommandFormat, QuoteShellValue(remoteDirectory));
            var mkdirResult = await _commandService.RunAdbShellAsync(serial, mkdirCommand, cancellationToken).ConfigureAwait(false);
            if (mkdirResult.ExitCode != 0)
                return false;

            foreach (var obbFile in packageInfo.ObbFiles)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var remotePath = $"{remoteDirectory}/{obbFile.FileName}";
                var pushArguments = string.Format(
                    AdbInstallConstants.PushArgumentsFormat,
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
                AdbInstallConstants.InstallApkArgumentsFormat,
                CreateInstallOptions(options),
                QuoteProcessArgument(apkPath));
        }

        private static string CreateInstallOptions(InstallPackageOptions options)
        {
            var grantPermissionsArgument = options.GrantPermissions ? GrantPermissionsArgument : EmptyOption;
            var allowDowngradeArgument = options.AllowDowngrade ? AdbInstallConstants.AllowDowngradeArgument : EmptyOption;
            return $"{grantPermissionsArgument}{allowDowngradeArgument}";
        }

        private static InstallPackageResult ParseInstallResult(string filePath, CommandResult result)
        {
            var output = $"{result.StandardOutput}\n{result.StandardError}";
            if (output.Contains(AdbInstallConstants.SuccessOutputToken, StringComparison.OrdinalIgnoreCase))
                return CreateSuccess(filePath);

            var failureCode = ExtractFailureCode(output);
            if (!string.IsNullOrWhiteSpace(failureCode))
                return CreateAdbFailure(filePath, failureCode);

            return result.ExitCode == 0
                ? CreateSuccess(filePath)
                : CreateFailure(filePath, DeviceLogResourceKeys.InstallPackageAdbFailure);
        }

        private static string ExtractFailureCode(string output)
        {
            var start = output.IndexOf(AdbInstallConstants.FailureOutputPrefix, StringComparison.OrdinalIgnoreCase);
            if (start < 0)
                return string.Empty;

            start += AdbInstallConstants.FailureOutputPrefix.Length;
            var end = output.IndexOf(AdbInstallConstants.FailureOutputSuffix, start, StringComparison.OrdinalIgnoreCase);
            if (end <= start)
                return string.Empty;

            return output[start..end].Trim();
        }

        private static InstallPackageResult CreateAdbFailure(string filePath, string failureCode)
        {
            var failureCategory = ExtractFailureCategory(failureCode);
            return failureCategory switch
            {
                AdbInstallConstants.AlreadyExistsFailureCode => CreateFailure(
                    filePath,
                    DeviceLogResourceKeys.InstallPackageAlreadyExists,
                    failureCode),
                AdbInstallConstants.VersionDowngradeFailureCode => CreateFailure(
                    filePath,
                    DeviceLogResourceKeys.InstallPackageVersionDowngrade,
                    failureCode),
                AdbInstallConstants.InsufficientStorageFailureCode => CreateFailure(
                    filePath,
                    DeviceLogResourceKeys.InstallPackageInsufficientStorage,
                    failureCode),
                AdbInstallConstants.InvalidApkFailureCode => CreateFailure(
                    filePath,
                    DeviceLogResourceKeys.InstallPackageInvalidApk,
                    failureCode),
                AdbInstallConstants.NoMatchingAbisFailureCode => CreateFailure(
                    filePath,
                    DeviceLogResourceKeys.InstallPackageNoMatchingAbis,
                    failureCode),
                AdbInstallConstants.MissingSharedLibraryFailureCode => CreateFailure(
                    filePath,
                    DeviceLogResourceKeys.InstallPackageMissingSharedLibrary,
                    failureCode),
                _ => CreateFailure(
                    filePath,
                    DeviceLogResourceKeys.InstallPackageAdbFailureCodeFormat,
                    failureCode,
                    failureCode)
            };
        }

        private static string ExtractFailureCategory(string failureCode)
        {
            var detailSeparatorIndex = failureCode.IndexOf(AdbInstallConstants.FailureCodeDetailSeparator);
            return detailSeparatorIndex <= 0
                ? failureCode.Trim()
                : failureCode[..detailSeparatorIndex].Trim();
        }

        private static InstallPackageResult CreateSuccess(string filePath)
        {
            return new InstallPackageResult(filePath, true, DeviceLogResourceKeys.InstallPackageSuccess);
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
                AdbInstallConstants.TempInstallDirectoryName,
                AdbInstallConstants.TempInstallSubdirectoryName,
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
