using System.Windows;
using System.Windows.Controls;
using DeepDroidChanger.ViewModels;

namespace DeepDroidChanger.Views;

public sealed partial class ViewDeviceToolDetailWindow : Window
{
    public ViewDeviceToolDetailWindow(ViewDeviceViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);

        InitializeComponent();
        DataContext = viewModel;
    }

    public ViewDeviceToolEditor Editor { get; private set; }

    internal void SetEditor(ViewDeviceToolEditor editor)
    {
        Editor = editor;
        RenameContent.Visibility = editor == ViewDeviceToolEditor.Rename
            ? Visibility.Visible
            : Visibility.Collapsed;
        InputAdbContent.Visibility = editor == ViewDeviceToolEditor.InputAdb
            ? Visibility.Visible
            : Visibility.Collapsed;
        FileTransferContent.Visibility = editor == ViewDeviceToolEditor.FileTransfer
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    internal void FocusPrimaryInput()
    {
        Dispatcher.BeginInvoke(
            System.Windows.Threading.DispatcherPriority.Input,
            new Action(() =>
            {
                FrameworkElement? target = Editor switch
                {
                    ViewDeviceToolEditor.Rename => RenameTextBox,
                    ViewDeviceToolEditor.InputAdb => InputTextBox,
                    ViewDeviceToolEditor.FileTransfer => ComputerFilePathTextBox,
                    _ => null
                };
                target?.Focus();
                if (target is TextBox textBox)
                    textBox.SelectAll();
            }));
    }
}
