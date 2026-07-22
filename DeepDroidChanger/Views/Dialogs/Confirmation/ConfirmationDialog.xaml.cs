using System.Windows;

namespace DeepDroidChanger.Views;

public sealed partial class ConfirmationDialog : Window
{
    public ConfirmationDialog()
    {
        InitializeComponent();
        SizeToContent = SizeToContent.Height;
    }
}
