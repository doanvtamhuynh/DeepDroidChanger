using DeepDroidChanger.Constants;
using DeepDroidChanger.Models;
using System.IO;
using System.IO.Compression;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace DeepDroidChanger.Services
{
    public sealed class XapkPackageService : IXapkPackageService
    {
        private const string ApkSearchPattern = "*.apk";
        private const string ObbSearchPattern = "*.obb";
        private const string BaseApkFileName = "base.apk";

        private readonly ILogger<XapkPackageService> _logger;

        public XapkPackageService(ILogger<XapkPackageService> logger)
        {
            _logger = logger;
        }

        public async Task<XapkPackageInfo> ExtractAsync(
            string xapkPath,
            string outputDirectory,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ArgumentException.ThrowIfNullOrWhiteSpace(xapkPath);
            ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);

            Directory.CreateDirectory(outputDirectory);
            await ExtractArchiveSafelyAsync(xapkPath, outputDirectory, cancellationToken).ConfigureAwait(false);

            var manifestPath = Path.Combine(outputDirectory, AdbInstallConstants.XapkManifestFileName);
            if (!File.Exists(manifestPath))
                throw new InvalidDataException(AdbInstallConstants.XapkManifestFileName);

            var manifestJson = await File.ReadAllTextAsync(manifestPath, cancellationToken).ConfigureAwait(false);
            var packageName = ReadPackageName(manifestJson);
            if (string.IsNullOrWhiteSpace(packageName))
                throw new InvalidDataException(AdbInstallConstants.PackageNameJsonProperty);

            var apkFilePaths = Directory
                .EnumerateFiles(outputDirectory, ApkSearchPattern, SearchOption.AllDirectories)
                .OrderBy(GetApkSortOrder)
                .ThenBy(path => Path.GetFileName(path) ?? path, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (apkFilePaths.Length == 0)
                throw new InvalidDataException(AdbInstallConstants.ApkExtension);

            var obbFiles = Directory
                .EnumerateFiles(outputDirectory, ObbSearchPattern, SearchOption.AllDirectories)
                .Select(path => new ObbFileInfo(path, Path.GetFileName(path) ?? path))
                .ToArray();

            _logger.LogDebug(
                "Extracted XAPK. ApkCount: {ApkCount}. ObbCount: {ObbCount}.",
                apkFilePaths.Length,
                obbFiles.Length);

            return new XapkPackageInfo(packageName, apkFilePaths, obbFiles);
        }

        private static async Task ExtractArchiveSafelyAsync(
            string archivePath,
            string outputDirectory,
            CancellationToken cancellationToken)
        {
            string outputRoot = Path.GetFullPath(outputDirectory)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;

            await using var archiveStream = new FileStream(
                archivePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 81920,
                useAsync: true);
            using var archive = new ZipArchive(archiveStream, ZipArchiveMode.Read, leaveOpen: false);

            foreach (ZipArchiveEntry entry in archive.Entries)
            {
                cancellationToken.ThrowIfCancellationRequested();

                string destinationPath = Path.GetFullPath(Path.Combine(outputRoot, entry.FullName));
                if (!destinationPath.StartsWith(outputRoot, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("XAPK entry escapes the extraction directory.");

                if (string.IsNullOrEmpty(entry.Name))
                {
                    Directory.CreateDirectory(destinationPath);
                    continue;
                }

                string? destinationDirectory = Path.GetDirectoryName(destinationPath);
                if (!string.IsNullOrWhiteSpace(destinationDirectory))
                    Directory.CreateDirectory(destinationDirectory);

                await using Stream source = entry.Open();
                await using var destination = new FileStream(
                    destinationPath,
                    FileMode.Create,
                    FileAccess.Write,
                    FileShare.None,
                    bufferSize: 81920,
                    useAsync: true);
                await source.CopyToAsync(destination, cancellationToken).ConfigureAwait(false);
            }
        }

        private static int GetApkSortOrder(string apkPath)
        {
            return string.Equals(Path.GetFileName(apkPath), BaseApkFileName, StringComparison.OrdinalIgnoreCase)
                ? 0
                : 1;
        }

        private static string ReadPackageName(string manifestJson)
        {
            using var document = JsonDocument.Parse(manifestJson);
            var root = document.RootElement;

            if (TryGetStringProperty(root, AdbInstallConstants.PackageNameJsonProperty, out var packageName))
                return packageName;

            if (TryGetStringProperty(root, AdbInstallConstants.AlternatePackageNameJsonProperty, out var alternatePackageName))
                return alternatePackageName;

            return string.Empty;
        }

        private static bool TryGetStringProperty(JsonElement element, string propertyName, out string value)
        {
            if (element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String)
            {
                value = property.GetString()?.Trim() ?? string.Empty;
                return !string.IsNullOrWhiteSpace(value);
            }

            value = string.Empty;
            return false;
        }
    }
}
