using DeepDroidChanger.Models;
using DeepDroidChanger.ViewModels;
using DeepDroidChanger.Views;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Windows;

namespace DeepDroidChanger.Services;

public sealed class BackupConfigDialogService : IBackupConfigDialogService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<BackupConfigDialogService> _logger;

    public BackupConfigDialogService(
        IServiceScopeFactory scopeFactory,
        ILogger<BackupConfigDialogService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public Task<DeviceBackupOptions?> ShowBackupConfigAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var scope = _scopeFactory.CreateScope();
        BackupDeviceViewModel viewModel = scope.ServiceProvider
            .GetRequiredService<BackupDeviceViewModel>();
        BackupDeviceDialog window = scope.ServiceProvider
            .GetRequiredService<BackupDeviceDialog>();
        window.Owner = Application.Current?.MainWindow;
        window.DataContext = viewModel;

        viewModel.CloseRequested += accepted => window.DialogResult = accepted;
        _logger.LogDebug("Opening the shared Backup Device configuration dialog.");

        using CancellationTokenRegistration registration =
            DialogCancellation.RegisterClose(window, cancellationToken);
        bool accepted = window.ShowDialog() == true;
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<DeviceBackupOptions?>(
            accepted ? viewModel.BuildOptions() : null);
    }
}
