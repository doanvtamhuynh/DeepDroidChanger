namespace DeepDroidChanger.Models
{
    public sealed class XapkPackageInfo
    {
        public XapkPackageInfo(string packageName, IReadOnlyList<string> apkFilePaths, IReadOnlyList<ObbFileInfo> obbFiles)
        {
            PackageName = packageName;
            ApkFilePaths = apkFilePaths;
            ObbFiles = obbFiles;
        }

        public string PackageName { get; }
        public IReadOnlyList<string> ApkFilePaths { get; }
        public IReadOnlyList<ObbFileInfo> ObbFiles { get; }
    }
}
