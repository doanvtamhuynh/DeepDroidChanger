using System.Windows;
using DeepDroidChanger.Models;
using DeepDroidChanger.ViewModels;
using DeepDroidChanger.Views;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DeepDroidChanger.Services;

public sealed class FakeProxyBatchDialogService : IFakeProxyBatchDialogService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<FakeProxyBatchDialogService> _logger;

    public FakeProxyBatchDialogService(
        IServiceScopeFactory scopeFactory,
        ILogger<FakeProxyBatchDialogService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task<FakeProxyBatchDialogResult?> ShowFakeProxyBatchAsync(
        int targetCount,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        _logger.LogDebug(
            "Opening Multiple Device Fake Proxy dialog. Target count: {TargetCount}.",
            targetCount);
        using var scope = _scopeFactory.CreateScope();

        FakeProxyBatchViewModel viewModel = scope.ServiceProvider
            .GetRequiredService<FakeProxyBatchViewModel>();
        await viewModel.InitializeAsync(targetCount, cancellationToken).ConfigureAwait(true);

        FakeProxyBatchDialog window = scope.ServiceProvider
            .GetRequiredService<FakeProxyBatchDialog>();
        window.Owner = Application.Current?.MainWindow;
        window.DataContext = viewModel;
        viewModel.CloseRequested += (_, result) => window.DialogResult = result;

        using CancellationTokenRegistration cancellationRegistration =
            DialogCancellation.RegisterClose(window, cancellationToken);
        bool dialogResult = window.ShowDialog() ?? false;
        cancellationToken.ThrowIfCancellationRequested();

        FakeProxyBatchDialogResult? result = dialogResult
            ? viewModel.BuildBatchResult()
            : null;
        _logger.LogDebug(
            "Multiple Device Fake Proxy dialog closed. Result: {HasResult}.",
            result != null);

        return result;
    }
}
