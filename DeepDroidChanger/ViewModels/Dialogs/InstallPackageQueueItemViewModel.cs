using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;

namespace DeepDroidChanger.ViewModels;

public sealed partial class InstallPackageQueueItemViewModel : ObservableObject
{
    public InstallPackageQueueItemViewModel(string filePath, string initialStatus)
    {
        FilePath = filePath;
        FileName = Path.GetFileName(filePath) ?? filePath;
        StatusText = initialStatus;
    }

    public string FilePath { get; }
    public string FileName { get; }

    [ObservableProperty]
    private string _statusText;

    [ObservableProperty]
    private int _progress;

    [ObservableProperty]
    private bool _isSuccessful;

    [ObservableProperty]
    private bool _isFailed;
}
