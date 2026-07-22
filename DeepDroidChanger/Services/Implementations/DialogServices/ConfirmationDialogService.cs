using DeepDroidChanger.Models;
using DeepDroidChanger.ViewModels;
using DeepDroidChanger.Views;
using Microsoft.Extensions.DependencyInjection;
using System.Windows;

namespace DeepDroidChanger.Services;

public sealed class ConfirmationDialogService : IConfirmationDialogService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILocalizationService _localizationService;

    public ConfirmationDialogService(
        IServiceScopeFactory scopeFactory,
        ILocalizationService localizationService)
    {
        _scopeFactory = scopeFactory;
        _localizationService = localizationService;
    }

    public Task<bool> ShowConfirmationAsync(
        ConfirmationDialogOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.Message);
        cancellationToken.ThrowIfCancellationRequested();

        string caption = !string.IsNullOrWhiteSpace(options.Caption)
            ? options.Caption
            : _localizationService.GetString("ConfirmationDialog_DefaultCaption");

        string confirmText = !string.IsNullOrWhiteSpace(options.ConfirmButtonText)
            ? options.ConfirmButtonText
            : _localizationService.GetString("ConfirmationDialog_YesButton");

        string cancelText = !string.IsNullOrWhiteSpace(options.CancelButtonText)
            ? options.CancelButtonText
            : _localizationService.GetString("ConfirmationDialog_NoButton");

        string? warningMessage = options.WarningMessage;
        if (warningMessage == null)
        {
            warningMessage = _localizationService.GetString("ConfirmationDialog_DefaultWarning");
        }

        using var scope = _scopeFactory.CreateScope();
        ConfirmationDialogViewModel viewModel =
            scope.ServiceProvider.GetRequiredService<ConfirmationDialogViewModel>();
        viewModel.Initialize(
            caption,
            options.Message,
            warningMessage,
            confirmText,
            cancelText,
            options.Icon);

        ConfirmationDialog window = scope.ServiceProvider.GetRequiredService<ConfirmationDialog>();

        Window? activeWindow = Application.Current?.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive)
                               ?? Application.Current?.MainWindow;
        if (activeWindow != null && activeWindow != window)
        {
            window.Owner = activeWindow;
        }

        window.DataContext = viewModel;
        viewModel.CloseRequested += (_, confirmed) =>
        {
            try
            {
                window.DialogResult = confirmed;
            }
            catch (InvalidOperationException)
            {
                window.Close();
            }
        };

        bool result = window.ShowDialog() ?? false;
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(result);
    }
}
