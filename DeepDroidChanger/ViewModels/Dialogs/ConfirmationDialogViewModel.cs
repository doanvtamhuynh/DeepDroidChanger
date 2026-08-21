using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DeepDroidChanger.Models;
using MaterialDesignThemes.Wpf;

namespace DeepDroidChanger.ViewModels;

public sealed partial class ConfirmationDialogViewModel : ObservableObject
{
    [ObservableProperty]
    private string _caption = string.Empty;

    [ObservableProperty]
    private string _message = string.Empty;

    [ObservableProperty]
    private string? _warningMessage;

    [ObservableProperty]
    private string _confirmButtonText = string.Empty;

    [ObservableProperty]
    private string _cancelButtonText = string.Empty;

    [ObservableProperty]
    private PackIconKind _iconKind = PackIconKind.HelpCircleOutline;

    public bool HasWarning => !string.IsNullOrWhiteSpace(WarningMessage);

    public event EventHandler<bool>? CloseRequested;

    public void Initialize(
        string caption,
        string message,
        string? warningMessage,
        string confirmButtonText,
        string cancelButtonText,
        ConfirmationDialogIcon icon = ConfirmationDialogIcon.Question)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(caption);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        ArgumentException.ThrowIfNullOrWhiteSpace(confirmButtonText);
        ArgumentException.ThrowIfNullOrWhiteSpace(cancelButtonText);

        Caption = caption;
        Message = message;
        WarningMessage = warningMessage;
        ConfirmButtonText = confirmButtonText;
        CancelButtonText = cancelButtonText;
        IconKind = icon switch
        {
            ConfirmationDialogIcon.ChangeDevice => PackIconKind.CellphoneCog,
            ConfirmationDialogIcon.Wipe => PackIconKind.DeleteSweep,
            ConfirmationDialogIcon.Sim => PackIconKind.SimCard,
            ConfirmationDialogIcon.Delete => PackIconKind.Delete,
            ConfirmationDialogIcon.Warning => PackIconKind.AlertOutline,
            _ => PackIconKind.HelpCircleOutline
        };
    }

    partial void OnWarningMessageChanged(string? value) => OnPropertyChanged(nameof(HasWarning));

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
