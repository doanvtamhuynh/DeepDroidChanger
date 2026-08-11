using System.Collections.ObjectModel;
using System.Collections.Specialized;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DeepDroidChanger.Models;
using DeepDroidChanger.Services;

namespace DeepDroidChanger.ViewModels;

public sealed partial class InstallPackageBatchViewModel : ObservableObject, IDisposable
{
    private readonly IFilePickerDialogService _filePickerDialogService;
    private readonly ILocalizationService _localizationService;
    private InstallPackageBatchRequest? _request;
    private bool _isDisposed;

    public InstallPackageBatchViewModel(
        IFilePickerDialogService filePickerDialogService,
        ILocalizationService localizationService)
    {
        _filePickerDialogService = filePickerDialogService;
        _localizationService = localizationService;
        Packages.CollectionChanged += OnPackagesChanged;
    }

    public ObservableCollection<InstallPackageQueueItemViewModel> Packages { get; } = new();

    public event EventHandler<bool>? CloseRequested;

    [ObservableProperty]
    private int _batchTargetCount;

    [ObservableProperty]
    private string _deviceInfoText = string.Empty;

    [ObservableProperty]
    private InstallPackageQueueItemViewModel? _selectedPackage;

    [ObservableProperty]
    private bool _grantPermissions = true;

    [ObservableProperty]
    private bool _allowDowngrade;

    public void InitializeBatch(int targetCount)
    {
        BatchTargetCount = targetCount;
        UpdateDeviceInfoText();
    }

    partial void OnBatchTargetCountChanged(int value)
    {
        UpdateDeviceInfoText();
    }

    partial void OnSelectedPackageChanged(InstallPackageQueueItemViewModel? value)
    {
        RemoveSelectedPackageCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    private void AddFiles()
    {
        string filter = _localizationService.GetString("Log_InstallPackageFilePickerFilter");
        string title = _localizationService.GetString("Log_InstallPackageFilePickerTitle");
        IReadOnlyList<string> selectedFiles = _filePickerDialogService
            .ShowOpenFileDialogMulti(filter, title);

        foreach (string filePath in selectedFiles)
        {
            if (Packages.Any(item => string.Equals(
                    item.FilePath,
                    filePath,
                    StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            Packages.Add(new InstallPackageQueueItemViewModel(filePath));
        }
    }

    [RelayCommand(CanExecute = nameof(CanRemoveSelectedPackage))]
    private void RemoveSelectedPackage()
    {
        if (SelectedPackage == null)
            return;

        Packages.Remove(SelectedPackage);
        SelectedPackage = null;
    }

    [RelayCommand(CanExecute = nameof(CanStartInstall))]
    private Task StartInstallAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _request = BuildRequestCore();
        if (_request == null)
            return Task.CompletedTask;

        CloseRequested?.Invoke(this, true);
        return Task.CompletedTask;
    }

    public InstallPackageBatchRequest? BuildRequest()
    {
        return _request ?? BuildRequestCore();
    }

    public void Dispose()
    {
        if (_isDisposed)
            return;

        _isDisposed = true;
        Packages.CollectionChanged -= OnPackagesChanged;
    }

    private bool CanRemoveSelectedPackage() => SelectedPackage != null;

    private bool CanStartInstall() => Packages.Count > 0;

    private void OnPackagesChanged(object? sender, NotifyCollectionChangedEventArgs args)
    {
        StartInstallCommand.NotifyCanExecuteChanged();
        RemoveSelectedPackageCommand.NotifyCanExecuteChanged();
    }

    private InstallPackageBatchRequest? BuildRequestCore()
    {
        if (Packages.Count == 0)
            return null;

        string[] filePaths = Packages
            .Select(package => package.FilePath)
            .ToArray();

        return new InstallPackageBatchRequest(
            Array.AsReadOnly(filePaths),
            new InstallPackageOptions(GrantPermissions, AllowDowngrade));
    }

    private void UpdateDeviceInfoText()
    {
        string format = _localizationService.GetString("InstallPackage_BatchDeviceInfo");
        DeviceInfoText = string.Format(format, BatchTargetCount);
    }
}
