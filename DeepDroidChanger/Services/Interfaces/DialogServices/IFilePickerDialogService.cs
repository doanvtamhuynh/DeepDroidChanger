namespace DeepDroidChanger.Services
{
    public interface IFilePickerDialogService
    {
        string? ShowOpenFileDialog(string filter, string title);
        IReadOnlyList<string> ShowOpenFileDialogMulti(string filter, string title);
    }
}
