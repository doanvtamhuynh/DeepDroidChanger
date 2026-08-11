using System.IO;

namespace DeepDroidChanger.ViewModels;

public sealed class InstallPackageQueueItemViewModel
{
    public InstallPackageQueueItemViewModel(string filePath)
    {
        FilePath = filePath;
        FileName = Path.GetFileName(filePath) ?? filePath;
    }

    public string FilePath { get; }
    public string FileName { get; }
}
