using DeepDroidChanger.Models;
using DeepDroidChanger.ViewModels;
using DeepDroidChanger.Views;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Windows;

namespace DeepDroidChanger.Services;

public sealed class RandomDeviceInfoDialogService : IRandomDeviceInfoDialogService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<RandomDeviceInfoDialogService> _logger;

    public RandomDeviceInfoDialogService(
        IServiceScopeFactory scopeFactory,
        ILogger<RandomDeviceInfoDialogService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public Task<bool> ShowRandomDeviceInfoAsync(
        DeviceInfoApiDevice device,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(device);
        cancellationToken.ThrowIfCancellationRequested();

        _logger.LogDebug("Opening full random device information dialog.");
        using var scope = _scopeFactory.CreateScope();
        var viewModel = scope.ServiceProvider.GetRequiredService<RandomDeviceInfoViewModel>();
        viewModel.Initialize(device);

        var window = scope.ServiceProvider.GetRequiredService<RandomDeviceInfoDialog>();
        window.Owner = Application.Current?.MainWindow;
        window.DataContext = viewModel;
        viewModel.UpdateRequested += (_, _) => window.DialogResult = true;

        bool updated = window.ShowDialog() ?? false;
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(updated);
    }
}
