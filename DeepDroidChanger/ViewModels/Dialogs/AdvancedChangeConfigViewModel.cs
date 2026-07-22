using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DeepDroidChanger.Helpers;
using DeepDroidChanger.Models;
using DeepDroidChanger.Services;
using Microsoft.Extensions.Logging;
using System.Collections.ObjectModel;

namespace DeepDroidChanger.ViewModels;

public sealed partial class AdvancedChangeConfigViewModel : ObservableObject
{
    private readonly IDevicePackageService _packageService;
    private readonly ILocalizationService _localizationService;
    private readonly ILogger<AdvancedChangeConfigViewModel> _logger;
    private IReadOnlyList<string> _loadedPackages = [];
    private string _deviceSerial = string.Empty;

    [ObservableProperty]
    private bool _changeAndroidId;

    [ObservableProperty]
    private bool _changeMacAddress = true;

    [ObservableProperty]
    private bool _useIntegritySecurityPatch = true;

    [ObservableProperty]
    private bool _useRmRfForPackageCleanup;

    [ObservableProperty]
    private bool _clearAllPackages;

    [ObservableProperty]
    private bool _clearSelectedPackages;

    [ObservableProperty]
    private bool _clearGooglePackages;

    [ObservableProperty]
    private bool _clearGoogleAccounts = true;

    [ObservableProperty]
    private PackageListScopeOption? _selectedPackageScope;

    [ObservableProperty]
    private string? _selectedAvailablePackage;

    [ObservableProperty]
    private string? _selectedWipePackage;

    [ObservableProperty]
    private bool _isLoadingPackages;

    [ObservableProperty]
    private string _packageLoadStatus = string.Empty;

    [ObservableProperty]
    private string _validationMessage = string.Empty;

    public AdvancedChangeConfigViewModel(
        IDevicePackageService packageService,
        ILocalizationService localizationService,
        ILogger<AdvancedChangeConfigViewModel> logger)
    {
        _packageService = packageService;
        _localizationService = localizationService;
        _logger = logger;
        AvailablePackages = new ObservableCollection<string>();
        SelectedPackages = new ObservableCollection<string>();
        PackageScopes =
        [
            new PackageListScopeOption(
                PackageListScope.All,
                _localizationService.GetString("AdvancedChangeConfig_AllPackages")),
            new PackageListScopeOption(
                PackageListScope.User,
                _localizationService.GetString("AdvancedChangeConfig_UserPackages"))
        ];
        SelectedPackageScope = PackageScopes[0];
    }

    public ObservableCollection<string> AvailablePackages { get; }

    public ObservableCollection<string> SelectedPackages { get; }

    public IReadOnlyList<PackageListScopeOption> PackageScopes { get; }

    public bool IsSelectiveWipeEnabled => !ClearAllPackages;

    public bool IsPackageSelectionActive => !ClearAllPackages && ClearSelectedPackages;

    public event EventHandler<AdvancedChangeConfigDialogResult?>? CloseRequested;

    public void Initialize(
        string deviceSerial,
        DeviceChangeOptions options,
        bool useIntegritySecurityPatch = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceSerial);
        ArgumentNullException.ThrowIfNull(options);

        _deviceSerial = deviceSerial;
        ChangeAndroidId = options.ChangeAndroidId;
        ChangeMacAddress = options.ChangeMacAddress;
        UseIntegritySecurityPatch = useIntegritySecurityPatch;
        UseRmRfForPackageCleanup = options.UseRmRfForPackageCleanup;
        ClearAllPackages = options.ClearAllPackages;
        ClearSelectedPackages = options.ClearSelectedPackages;
        ClearGooglePackages = options.ClearGooglePackages;
        ClearGoogleAccounts = options.ClearGoogleAccounts;

        _loadedPackages = [];
        AvailablePackages.Clear();
        SelectedPackages.Clear();
        foreach (string packageName in DeviceChangeOptionsHelper.NormalizePackageNames(options.SelectedPackages))
            SelectedPackages.Add(packageName);

        PackageLoadStatus = _localizationService.GetString("AdvancedChangeConfig_LoadPackagesPrompt");
        RefreshValidation();
    }

    partial void OnClearAllPackagesChanged(bool value)
    {
        OnPropertyChanged(nameof(IsSelectiveWipeEnabled));
        OnPropertyChanged(nameof(IsPackageSelectionActive));
        LoadPackagesCommand.NotifyCanExecuteChanged();
        RefreshValidation();
    }

    partial void OnClearSelectedPackagesChanged(bool value)
    {
        OnPropertyChanged(nameof(IsPackageSelectionActive));
        LoadPackagesCommand.NotifyCanExecuteChanged();
        RefreshValidation();
    }

    partial void OnSelectedPackageScopeChanged(PackageListScopeOption? value)
    {
        LoadPackagesCommand.NotifyCanExecuteChanged();
    }

    partial void OnSelectedAvailablePackageChanged(string? value)
    {
        AddSelectedPackageCommand.NotifyCanExecuteChanged();
    }

    partial void OnSelectedWipePackageChanged(string? value)
    {
        RemoveSelectedPackageCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsLoadingPackagesChanged(bool value)
    {
        LoadPackagesCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanLoadPackages))]
    private async Task LoadPackagesAsync(CancellationToken cancellationToken)
    {
        PackageListScopeOption scope = SelectedPackageScope
            ?? throw new InvalidOperationException("A package scope must be selected.");

        IsLoadingPackages = true;
        PackageLoadStatus = _localizationService.GetString("AdvancedChangeConfig_LoadingPackages");
        try
        {
            _loadedPackages = scope.Scope == PackageListScope.User
                ? await _packageService
                    .GetUserInstalledPackagesAsync(_deviceSerial, cancellationToken)
                    .ConfigureAwait(true)
                : await _packageService
                    .GetInstalledPackagesAsync(_deviceSerial, cancellationToken)
                    .ConfigureAwait(true);
            RefreshAvailablePackages();
            PackageLoadStatus = string.Format(
                _localizationService.GetString("AdvancedChangeConfig_LoadedPackages"),
                _loadedPackages.Count);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                exception,
                "Failed to load packages for device {Serial} in Advanced Change Device configuration.",
                _deviceSerial);
            _loadedPackages = [];
            AvailablePackages.Clear();
            PackageLoadStatus = _localizationService.GetString("AdvancedChangeConfig_LoadPackagesFailed");
        }
        finally
        {
            IsLoadingPackages = false;
        }
    }

    private bool CanLoadPackages()
    {
        return IsPackageSelectionActive
            && !IsLoadingPackages
            && SelectedPackageScope != null;
    }

    [RelayCommand(CanExecute = nameof(CanAddSelectedPackage))]
    private void AddSelectedPackage()
    {
        if (SelectedAvailablePackage is not { Length: > 0 } packageName)
            return;

        AddPackages([packageName]);
        SelectedAvailablePackage = null;
    }

    private bool CanAddSelectedPackage()
    {
        return !string.IsNullOrWhiteSpace(SelectedAvailablePackage);
    }

    [RelayCommand(CanExecute = nameof(CanRemoveSelectedPackage))]
    private void RemoveSelectedPackage()
    {
        if (SelectedWipePackage is not { Length: > 0 } packageName)
            return;

        SelectedPackages.Remove(packageName);
        SelectedWipePackage = null;
        RefreshAvailablePackages();
        RefreshValidation();
    }

    private bool CanRemoveSelectedPackage()
    {
        return !string.IsNullOrWhiteSpace(SelectedWipePackage);
    }

    [RelayCommand]
    private void AddAllPackages()
    {
        AddPackages(AvailablePackages.ToArray());
    }

    [RelayCommand]
    private void RemoveAllPackages()
    {
        SelectedPackages.Clear();
        SelectedWipePackage = null;
        RefreshAvailablePackages();
        RefreshValidation();
    }

    [RelayCommand(CanExecute = nameof(CanConfirm))]
    private void Confirm()
    {
        var options = new DeviceChangeOptions
        {
            UseDefaultMode = false,
            ChangeAndroidId = ChangeAndroidId,
            ChangeMacAddress = ChangeMacAddress,
            UseRmRfForPackageCleanup = UseRmRfForPackageCleanup,
            ClearAllPackages = ClearAllPackages,
            ClearSelectedPackages = ClearSelectedPackages,
            ClearGooglePackages = ClearGooglePackages,
            ClearGoogleAccounts = ClearGoogleAccounts,
            SelectedPackages = DeviceChangeOptionsHelper.NormalizePackageNames(SelectedPackages)
        };
        CloseRequested?.Invoke(this, new AdvancedChangeConfigDialogResult(options, UseIntegritySecurityPatch));
    }

    private bool CanConfirm()
    {
        return ClearAllPackages
            || !ClearSelectedPackages
            || SelectedPackages.Count > 0;
    }

    private void AddPackages(IEnumerable<string> packageNames)
    {
        var selected = SelectedPackages.ToHashSet(StringComparer.Ordinal);
        foreach (string packageName in DeviceChangeOptionsHelper.NormalizePackageNames(packageNames))
        {
            if (selected.Add(packageName))
                SelectedPackages.Add(packageName);
        }

        SortCollection(SelectedPackages);
        RefreshAvailablePackages();
        RefreshValidation();
    }

    private void RefreshAvailablePackages()
    {
        var selected = SelectedPackages.ToHashSet(StringComparer.Ordinal);
        AvailablePackages.Clear();
        foreach (string packageName in DeviceChangeOptionsHelper
                     .NormalizePackageNames(_loadedPackages)
                     .Where(value => !selected.Contains(value)))
            AvailablePackages.Add(packageName);
    }

    private void RefreshValidation()
    {
        bool missingSelection = !ClearAllPackages
            && ClearSelectedPackages
            && SelectedPackages.Count == 0;
        ValidationMessage = missingSelection
            ? _localizationService.GetString("AdvancedChangeConfig_SelectPackageValidation")
            : string.Empty;
        ConfirmCommand.NotifyCanExecuteChanged();
    }

    private static void SortCollection(ObservableCollection<string> collection)
    {
        List<string> sorted = DeviceChangeOptionsHelper.NormalizePackageNames(collection);
        collection.Clear();
        foreach (string value in sorted)
            collection.Add(value);
    }
}
