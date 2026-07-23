using System;
using System.Windows;
using DeepDroidChanger.ViewModels;

namespace DeepDroidChanger.Views
{
    public sealed partial class ChangeLocationDialog : Window
    {
        public ChangeLocationDialog()
        {
            InitializeComponent();
        }

        private void CountryComboBox_DropDownClosed(object sender, EventArgs e)
        {
            if (DataContext is ChangeLocationViewModel viewModel)
            {
                viewModel.ApplySelectedLocationCoordinatesCommand.Execute(null);
            }
        }

        private void LocationComboBox_DropDownClosed(object sender, EventArgs e)
        {
            if (DataContext is ChangeLocationViewModel viewModel)
            {
                viewModel.ApplySelectedLocationCoordinatesCommand.Execute(null);
            }
        }
    }
}
