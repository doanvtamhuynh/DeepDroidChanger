namespace DeepDroidChanger.Services
{
    public interface IFilePickerDialogService
    {
        string? ShowOpenFileDialog(string filter, string title);
        IReadOnlyList<string> ShowOpenFileDialogMulti(string filter, string title);
        string? ShowSaveFileDialog(string filter, string title, string defaultFileName);
    }
}
