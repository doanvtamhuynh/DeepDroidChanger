using DeepDroidChanger.Helpers;
using DeepDroidChanger.Models;

namespace DeepDroidChanger.Tests.Helpers;

[TestClass]
public sealed class DeviceChangeOptionsHelperTests
{
    [TestMethod]
    public void CreateNormalizedCopy_PreservesFlagsOverridesModeAndNormalizesPackages()
    {
        var source = new DeviceChangeOptions
        {
            UseDefaultMode = true,
            ChangeAndroidId = true,
            ChangeMacAddress = false,
            UseRmRfForPackageCleanup = true,
            ClearAllPackages = false,
            ClearSelectedPackages = true,
            ClearGooglePackages = true,
            ClearGoogleAccounts = false,
            SelectedPackages = [" com.z ", "com.a", "com.z", " "]
        };

        DeviceChangeOptions result = DeviceChangeOptionsHelper.CreateNormalizedCopy(
            source,
            useDefaultMode: false);

        Assert.IsFalse(result.UseDefaultMode);
        Assert.IsTrue(result.ChangeAndroidId);
        Assert.IsFalse(result.ChangeMacAddress);
        Assert.IsTrue(result.UseRmRfForPackageCleanup);
        Assert.IsFalse(result.ClearAllPackages);
        Assert.IsTrue(result.ClearSelectedPackages);
        Assert.IsTrue(result.ClearGooglePackages);
        Assert.IsFalse(result.ClearGoogleAccounts);
        CollectionAssert.AreEqual(new[] { "com.a", "com.z" }, result.SelectedPackages);
        Assert.AreNotSame(source.SelectedPackages, result.SelectedPackages);
    }

    [TestMethod]
    public void HasPackageCleanup_ReflectsDefaultAndAdvancedCleanupSelections()
    {
        Assert.IsTrue(DeviceChangeOptionsHelper.HasPackageCleanup(
            new DeviceChangeOptions { UseDefaultMode = true }));
        Assert.IsTrue(DeviceChangeOptionsHelper.HasPackageCleanup(
            new DeviceChangeOptions
            {
                UseDefaultMode = false,
                ClearAllPackages = false,
                ClearGoogleAccounts = false,
                ClearSelectedPackages = true
            }));
        Assert.IsFalse(DeviceChangeOptionsHelper.HasPackageCleanup(
            new DeviceChangeOptions
            {
                UseDefaultMode = false,
                ClearAllPackages = false,
                ClearGoogleAccounts = false
            }));
    }
}
