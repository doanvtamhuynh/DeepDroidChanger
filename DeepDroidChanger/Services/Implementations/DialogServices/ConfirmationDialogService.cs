using DeepDroidChanger.ViewModels;
using DeepDroidChanger.Views;
using Microsoft.Extensions.DependencyInjection;
using System.Windows;

namespace DeepDroidChanger.Services;

public sealed class ConfirmationDialogService : IConfirmationDialogService
{
    private readonly IServiceScopeFactory _scopeFactory;

    public ConfirmationDialogService(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    public Task<bool> ShowWarningConfirmationAsync(
        string message,
        string caption,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        ArgumentException.ThrowIfNullOrWhiteSpace(caption);
        cancellationToken.ThrowIfCancellationRequested();

        using var scope = _scopeFactory.CreateScope();
        ConfirmationDialogViewModel viewModel =
            scope.ServiceProvider.GetRequiredService<ConfirmationDialogViewModel>();
        viewModel.Initialize(caption, message);

        ConfirmationDialog window = scope.ServiceProvider.GetRequiredService<ConfirmationDialog>();
        window.Owner = Application.Current?.MainWindow;
        window.DataContext = viewModel;
        viewModel.CloseRequested += (_, confirmed) => window.DialogResult = confirmed;

        bool result = window.ShowDialog() ?? false;
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(result);
    }
}
