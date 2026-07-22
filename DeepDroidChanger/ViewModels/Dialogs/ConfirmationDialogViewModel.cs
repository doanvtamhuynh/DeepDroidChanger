using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace DeepDroidChanger.ViewModels;

public sealed partial class ConfirmationDialogViewModel : ObservableObject
{
    [ObservableProperty]
    private string _caption = string.Empty;

    [ObservableProperty]
    private string _message = string.Empty;

    public event EventHandler<bool>? CloseRequested;

    public void Initialize(string caption, string message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(caption);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);

        Caption = caption;
        Message = message;
    }

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
