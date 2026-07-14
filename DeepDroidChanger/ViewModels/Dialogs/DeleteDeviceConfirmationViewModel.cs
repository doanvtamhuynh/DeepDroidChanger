using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace DeepDroidChanger.ViewModels
{
    public sealed partial class DeleteDeviceConfirmationViewModel : ObservableObject
    {
        [ObservableProperty]
        private string _deviceName = string.Empty;

        [ObservableProperty]
        private string _deviceSerial = string.Empty;

        public event EventHandler<bool>? CloseRequested;

        [RelayCommand]
        private void Confirm()
        {
            CloseRequested?.Invoke(this, true);
        }

        [RelayCommand]
        private void Cancel()
        {
            CloseRequested?.Invoke(this, false);
        }
    }
}
