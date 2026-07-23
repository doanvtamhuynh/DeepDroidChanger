using DeepDroidChanger.Helpers;

namespace DeepDroidChanger.Tests.Helpers;

[TestClass]
public sealed class AssetDataReaderTests
{
    [TestMethod]
    public void ReadText_AllBundledDataAssets_ReturnsContent()
    {
        string[] resourcePaths =
        [
            "Assets/Data/bip0039.txt",
            "Assets/Data/carriers.json",
            "Assets/Data/imei_tacs.json",
            "Assets/Data/mac_vendors.json",
            "Assets/Data/names.txt",
            "Assets/Data/location-timezones.json",
        ];

        foreach (string resourcePath in resourcePaths)
            Assert.IsFalse(string.IsNullOrWhiteSpace(AssetDataReader.ReadText(resourcePath)), resourcePath);
    }

    [TestMethod]
    public void ReadText_PathOutsideDataDirectory_ThrowsInvalidOperationException()
    {
        Assert.ThrowsExactly<InvalidOperationException>(
            () => AssetDataReader.ReadText("Assets/Tools/platform-tools/adb.exe"));
        Assert.ThrowsExactly<InvalidOperationException>(
            () => AssetDataReader.ReadText("Assets/Data/../Icons/flag_en.ico"));
    }

    [TestMethod]
    public void ReadText_MissingEmbeddedAsset_ThrowsFileNotFoundException()
    {
        Assert.ThrowsExactly<FileNotFoundException>(
            () => AssetDataReader.ReadText("Assets/Data/missing.json"));
    }
}
