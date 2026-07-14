namespace DeepDroidChanger.Models
{
    public sealed class ObbFileInfo
    {
        public ObbFileInfo(string localPath, string fileName)
        {
            LocalPath = localPath;
            FileName = fileName;
        }

        public string LocalPath { get; }
        public string FileName { get; }
    }
}
