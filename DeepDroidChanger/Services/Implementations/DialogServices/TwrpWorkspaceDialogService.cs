using System.Windows;
using DeepDroidChanger.Models;
using DeepDroidChanger.ViewModels;
using DeepDroidChanger.Views;
using Microsoft.Extensions.DependencyInjection;

namespace DeepDroidChanger.Services;

public sealed class TwrpWorkspaceDialogService(IServiceScopeFactory scopeFactory) : ITwrpWorkspaceDialogService
{
    public Task<TwrpDialogResult?> ShowBackupAsync(string serial, CancellationToken cancellationToken) => ShowBackupAsync(serial, "", cancellationToken);
    public Task<TwrpDialogResult?> ShowRestoreAsync(string serial, CancellationToken cancellationToken) => ShowRestoreAsync(serial, "", cancellationToken);
    public Task<TwrpDialogResult?> ShowBackupAsync(string serial, string deviceName, CancellationToken cancellationToken) => ShowAsync(serial, deviceName, false, cancellationToken);
    public Task<TwrpDialogResult?> ShowRestoreAsync(string serial, string deviceName, CancellationToken cancellationToken) => ShowAsync(serial, deviceName, true, cancellationToken);
    private async Task<TwrpDialogResult?> ShowAsync(string serial, string deviceName, bool restore, CancellationToken token)
    {
        using var scope = scopeFactory.CreateScope();
        var vm = scope.ServiceProvider.GetRequiredService<TwrpWorkspaceViewModel>();
        await vm.InitializeAsync(serial, restore, token, deviceName);
        token.ThrowIfCancellationRequested();
        Window window = restore ? scope.ServiceProvider.GetRequiredService<TwrpRestoreDialog>() : scope.ServiceProvider.GetRequiredService<TwrpBackupDialog>();
        window.Owner = Application.Current?.MainWindow; window.DataContext = vm;
        TwrpDialogResult? result = null;
        vm.Submitted += value => { result = value; window.DialogResult = true; };
        using var registration = token.Register(() => window.Dispatcher.BeginInvoke(new Action(window.Close)));
        window.ShowDialog();
        return result;
    }
}
