using DeepDroidChanger.ViewModels;
using System.Windows.Controls;

namespace DeepDroidChanger.Views
{
    public sealed partial class SettingsView : UserControl
    {
        public SettingsView(SettingsViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;
        }
    }
}


