namespace DeepDroidChanger.Services
{
    public interface IFilePickerDialogService
    {
        string? ShowOpenFileDialog(
            string filter,
            string title,
            string? initialDirectory = null);
        IReadOnlyList<string> ShowOpenFileDialogMulti(
            string filter,
            string title,
            string? initialDirectory = null);
        string? ShowSaveFileDialog(string filter, string title, string defaultFileName);
        string? ShowOpenFolderDialog(string title, string? initialDirectory = null);
    }
}
