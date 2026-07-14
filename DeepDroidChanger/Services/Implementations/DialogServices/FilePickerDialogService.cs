using Microsoft.Win32;

namespace DeepDroidChanger.Services
{
    public sealed class FilePickerDialogService : IFilePickerDialogService
    {
        public string? ShowOpenFileDialog(string filter, string title)
        {
            var dialog = new OpenFileDialog
            {
                Filter = filter,
                Title = title
            };

            if (dialog.ShowDialog() == true)
            {
                return dialog.FileName;
            }

            return null;
        }

        public IReadOnlyList<string> ShowOpenFileDialogMulti(string filter, string title)
        {
            var dialog = new OpenFileDialog
            {
                Filter = filter,
                Title = title,
                Multiselect = true
            };

            if (dialog.ShowDialog() == true)
            {
                return dialog.FileNames;
            }

            return Array.Empty<string>();
        }
    }
}
