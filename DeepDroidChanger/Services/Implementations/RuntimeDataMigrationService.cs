using DeepDroidChanger.Constants;
using System.IO;
using Microsoft.Extensions.Logging;

namespace DeepDroidChanger.Services;

public sealed class RuntimeDataMigrationService : IRuntimeDataMigrationService
{
    private readonly ILogger<RuntimeDataMigrationService> _logger;
    private readonly string _applicationRootDirectory;

    public RuntimeDataMigrationService(ILogger<RuntimeDataMigrationService> logger)
        : this(AppContext.BaseDirectory, logger)
    {
    }

    internal RuntimeDataMigrationService(
        string applicationRootDirectory,
        ILogger<RuntimeDataMigrationService> logger)
    {
        _applicationRootDirectory = Path.GetFullPath(applicationRootDirectory);
        _logger = logger;
    }

    public void Migrate()
    {
        string legacyDirectory = Path.Combine(
            _applicationRootDirectory,
            AssetConstants.RuntimeData.LegacyDeviceManagerDirectoryName);
        if (!Directory.Exists(legacyDirectory))
            return;

        MigrateSingleDeviceData(legacyDirectory);
        MigrateMultipleDeviceData(legacyDirectory);
        DeleteLegacyDirectoryIfEmpty(legacyDirectory);
    }

    private void MigrateSingleDeviceData(string legacyDirectory)
    {
        string destination = Path.Combine(
            _applicationRootDirectory,
            AssetConstants.RuntimeData.ChangeSingleDeviceDirectoryName);
        bool hasLegacySingleData = File.Exists(Path.Combine(
                legacyDirectory,
                AssetConstants.RuntimeData.DevicesFileName))
            || Directory.EnumerateDirectories(legacyDirectory)
                .Any(path => !string.Equals(
                    Path.GetFileName(path),
                    AssetConstants.RuntimeData.LegacyMultipleDevicesDirectoryName,
                    StringComparison.OrdinalIgnoreCase));
        if (!hasLegacySingleData)
            return;

        if (Directory.Exists(destination))
        {
            _logger.LogWarning(
                "Skipped legacy Change Single Device data migration because destination {Destination} already exists.",
                destination);
            return;
        }

        Directory.CreateDirectory(destination);
        MoveFileIfPresent(
            Path.Combine(legacyDirectory, AssetConstants.RuntimeData.DevicesFileName),
            Path.Combine(destination, AssetConstants.RuntimeData.DevicesFileName));
        foreach (string directory in Directory.EnumerateDirectories(legacyDirectory))
        {
            if (string.Equals(
                    Path.GetFileName(directory),
                    AssetConstants.RuntimeData.LegacyMultipleDevicesDirectoryName,
                    StringComparison.OrdinalIgnoreCase))
                continue;

            Directory.Move(directory, Path.Combine(destination, Path.GetFileName(directory)));
        }
    }

    private void MigrateMultipleDeviceData(string legacyDirectory)
    {
        string source = Path.Combine(
            legacyDirectory,
            AssetConstants.RuntimeData.LegacyMultipleDevicesDirectoryName);
        if (!Directory.Exists(source))
            return;

        string destination = Path.Combine(
            _applicationRootDirectory,
            AssetConstants.RuntimeData.ChangeMultipleDevicesDirectoryName);
        if (Directory.Exists(destination))
        {
            _logger.LogWarning(
                "Skipped legacy Change Multiple Devices data migration because destination {Destination} already exists.",
                destination);
            return;
        }

        Directory.Move(source, destination);
    }

    private static void MoveFileIfPresent(string source, string destination)
    {
        if (File.Exists(source))
            File.Move(source, destination);
    }

    private static void DeleteLegacyDirectoryIfEmpty(string legacyDirectory)
    {
        if (!Directory.EnumerateFileSystemEntries(legacyDirectory).Any())
            Directory.Delete(legacyDirectory);
    }
}
