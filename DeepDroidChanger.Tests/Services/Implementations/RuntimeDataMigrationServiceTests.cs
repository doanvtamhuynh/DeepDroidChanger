using DeepDroidChanger.Constants;
using DeepDroidChanger.Services;
using DeepDroidChanger.Tests.Helpers;
using Microsoft.Extensions.Logging.Abstractions;

namespace DeepDroidChanger.Tests.Services.Implementations;

[TestClass]
[DoNotParallelize]
public sealed class RuntimeDataMigrationServiceTests
{
    [TestMethod]
    public void Migrate_SingleDeviceData_MovesIndexAndDeviceDirectories()
    {
        using var fixture = new TestTempDirectory();
        string legacy = CreateLegacyDirectory(fixture.Path);
        File.WriteAllText(Path.Combine(legacy, AssetConstants.RuntimeData.DevicesFileName), "[]");
        Directory.CreateDirectory(Path.Combine(legacy, "SERIAL"));

        CreateService(fixture.Path).Migrate();

        string destination = Path.Combine(
            fixture.Path,
            AssetConstants.RuntimeData.ChangeSingleDeviceDirectoryName);
        Assert.IsTrue(File.Exists(Path.Combine(destination, AssetConstants.RuntimeData.DevicesFileName)));
        Assert.IsTrue(Directory.Exists(Path.Combine(destination, "SERIAL")));
        Assert.IsFalse(Directory.Exists(legacy));
    }

    [TestMethod]
    public void Migrate_MultipleDeviceData_MovesDedicatedConfigurationDirectory()
    {
        using var fixture = new TestTempDirectory();
        string multipleDirectory = Path.Combine(
            CreateLegacyDirectory(fixture.Path),
            AssetConstants.RuntimeData.LegacyMultipleDevicesDirectoryName);
        Directory.CreateDirectory(multipleDirectory);
        File.WriteAllText(Path.Combine(multipleDirectory, "change_config.json"), "{}");

        CreateService(fixture.Path).Migrate();

        Assert.IsTrue(File.Exists(Path.Combine(
            fixture.Path,
            AssetConstants.RuntimeData.ChangeMultipleDevicesDirectoryName,
            "change_config.json")));
        Assert.IsFalse(Directory.Exists(Path.GetDirectoryName(multipleDirectory)!));
    }

    [TestMethod]
    public void Migrate_BothDataSets_MovesEachToItsDedicatedDirectory()
    {
        using var fixture = new TestTempDirectory();
        string legacy = CreateLegacyDirectory(fixture.Path);
        File.WriteAllText(Path.Combine(legacy, AssetConstants.RuntimeData.DevicesFileName), "[]");
        Directory.CreateDirectory(Path.Combine(legacy, "SERIAL"));
        Directory.CreateDirectory(Path.Combine(
            legacy,
            AssetConstants.RuntimeData.LegacyMultipleDevicesDirectoryName));

        CreateService(fixture.Path).Migrate();

        Assert.IsTrue(Directory.Exists(Path.Combine(
            fixture.Path,
            AssetConstants.RuntimeData.ChangeSingleDeviceDirectoryName,
            "SERIAL")));
        Assert.IsTrue(Directory.Exists(Path.Combine(
            fixture.Path,
            AssetConstants.RuntimeData.ChangeMultipleDevicesDirectoryName)));
        Assert.IsFalse(Directory.Exists(legacy));
    }

    [TestMethod]
    public void Migrate_AfterSuccessfulMigration_IsIdempotent()
    {
        using var fixture = new TestTempDirectory();
        string legacy = CreateLegacyDirectory(fixture.Path);
        File.WriteAllText(Path.Combine(legacy, AssetConstants.RuntimeData.DevicesFileName), "[]");
        RuntimeDataMigrationService service = CreateService(fixture.Path);

        service.Migrate();
        service.Migrate();

        Assert.IsTrue(File.Exists(Path.Combine(
            fixture.Path,
            AssetConstants.RuntimeData.ChangeSingleDeviceDirectoryName,
            AssetConstants.RuntimeData.DevicesFileName)));
        Assert.IsFalse(Directory.Exists(legacy));
    }

    [TestMethod]
    public void Migrate_WhenDestinationExists_PreservesLegacyData()
    {
        using var fixture = new TestTempDirectory();
        string legacy = CreateLegacyDirectory(fixture.Path);
        File.WriteAllText(Path.Combine(legacy, AssetConstants.RuntimeData.DevicesFileName), "legacy");
        string destination = Path.Combine(
            fixture.Path,
            AssetConstants.RuntimeData.ChangeSingleDeviceDirectoryName);
        Directory.CreateDirectory(destination);
        File.WriteAllText(Path.Combine(destination, AssetConstants.RuntimeData.DevicesFileName), "new");

        CreateService(fixture.Path).Migrate();

        Assert.AreEqual("new", File.ReadAllText(Path.Combine(
            destination,
            AssetConstants.RuntimeData.DevicesFileName)));
        Assert.AreEqual("legacy", File.ReadAllText(Path.Combine(
            legacy,
            AssetConstants.RuntimeData.DevicesFileName)));
    }

    [TestMethod]
    public void Migrate_WhenMultipleDestinationExists_PreservesLegacyConfiguration()
    {
        using var fixture = new TestTempDirectory();
        string legacyMultipleDirectory = Path.Combine(
            CreateLegacyDirectory(fixture.Path),
            AssetConstants.RuntimeData.LegacyMultipleDevicesDirectoryName);
        Directory.CreateDirectory(legacyMultipleDirectory);
        File.WriteAllText(Path.Combine(legacyMultipleDirectory, "change_config.json"), "legacy");
        string destination = Path.Combine(
            fixture.Path,
            AssetConstants.RuntimeData.ChangeMultipleDevicesDirectoryName);
        Directory.CreateDirectory(destination);
        File.WriteAllText(Path.Combine(destination, "change_config.json"), "new");

        CreateService(fixture.Path).Migrate();

        Assert.AreEqual("new", File.ReadAllText(Path.Combine(destination, "change_config.json")));
        Assert.AreEqual("legacy", File.ReadAllText(Path.Combine(
            legacyMultipleDirectory,
            "change_config.json")));
    }

    private static RuntimeDataMigrationService CreateService(string rootPath) =>
        new(rootPath, NullLogger<RuntimeDataMigrationService>.Instance);

    private static string CreateLegacyDirectory(string rootPath)
    {
        string directory = Path.Combine(
            rootPath,
            AssetConstants.RuntimeData.LegacyDeviceManagerDirectoryName);
        Directory.CreateDirectory(directory);
        return directory;
    }
}
