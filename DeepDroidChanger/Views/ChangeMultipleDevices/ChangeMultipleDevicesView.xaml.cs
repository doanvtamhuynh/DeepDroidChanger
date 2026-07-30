using DeepDroidChanger.ViewModels;
using System.Windows.Controls;

namespace DeepDroidChanger.Views
{
    public sealed partial class ChangeMultipleDevicesView : UserControl
    {
        public ChangeMultipleDevicesView(ChangeMultipleDevicesViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;
        }
    }
}
