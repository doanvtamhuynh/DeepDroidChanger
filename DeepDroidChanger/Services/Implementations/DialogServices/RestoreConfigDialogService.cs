using DeepDroidChanger.Models;
using DeepDroidChanger.ViewModels;
using DeepDroidChanger.Views;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Windows;

namespace DeepDroidChanger.Services;

public sealed class RestoreConfigDialogService : IRestoreConfigDialogService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<RestoreConfigDialogService> _logger;

    public RestoreConfigDialogService(
        IServiceScopeFactory scopeFactory,
        ILogger<RestoreConfigDialogService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public Task<DeviceRestoreOptions?> ShowRestoreConfigAsync(
        string targetSerial,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetSerial);
        cancellationToken.ThrowIfCancellationRequested();
        using var scope = _scopeFactory.CreateScope();
        RestoreDeviceViewModel viewModel = scope.ServiceProvider
            .GetRequiredService<RestoreDeviceViewModel>();
        RestoreDeviceDialog window = scope.ServiceProvider
            .GetRequiredService<RestoreDeviceDialog>();
        window.Owner = Application.Current?.MainWindow;
        window.DataContext = viewModel;
        viewModel.SetTargetDescription(targetSerial);
        viewModel.SetTargetSerials([targetSerial]);

        viewModel.CloseRequested += accepted => window.DialogResult = accepted;
        _logger.LogDebug("Opening the shared Restore Device configuration dialog.");

        using CancellationTokenRegistration registration =
            DialogCancellation.RegisterClose(window, cancellationToken);
        bool accepted = window.ShowDialog() == true;
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<DeviceRestoreOptions?>(
            accepted ? viewModel.BuildOptions() : null);
    }

    public Task<DeviceRestoreBatchOptions?> ShowRestoreBatchConfigAsync(
        IReadOnlyList<string> archivePaths,
        IReadOnlyList<string> targetSerials,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(archivePaths);
        ArgumentNullException.ThrowIfNull(targetSerials);
        if (archivePaths.Count == 0)
            throw new ArgumentException("At least one restore archive is required.", nameof(archivePaths));
        if (targetSerials.Count == 0)
            throw new ArgumentException("At least one target serial is required.", nameof(targetSerials));

        using var scope = _scopeFactory.CreateScope();
        RestoreDeviceViewModel viewModel = scope.ServiceProvider
            .GetRequiredService<RestoreDeviceViewModel>();
        RestoreDeviceDialog window = scope.ServiceProvider
            .GetRequiredService<RestoreDeviceDialog>();
        viewModel.SetArchivePaths(archivePaths);
        viewModel.SetTargetDescription(string.Join(", ", targetSerials));
        viewModel.SetTargetSerials(targetSerials);
        window.Owner = Application.Current?.MainWindow;
        window.DataContext = viewModel;

        viewModel.CloseRequested += accepted => window.DialogResult = accepted;
        _logger.LogDebug(
            "Opening the shared Restore Device configuration dialog for {ArchiveCount} archives.",
            archivePaths.Count);

        using CancellationTokenRegistration registration =
            DialogCancellation.RegisterClose(window, cancellationToken);
        bool accepted = window.ShowDialog() == true;
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<DeviceRestoreBatchOptions?>(
            accepted ? viewModel.BuildBatchOptions() : null);
    }
}
