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
    private IReadOnlyList<string> _deviceSerials = [];

    [ObservableProperty]
    private bool _changeAndroidId;

    [ObservableProperty]
    private bool _changeMacAddress = true;

    [ObservableProperty]
    private bool _useIntegritySecurityPatch = true;

    [ObservableProperty]
    private bool _updateIntegrity = true;

    [ObservableProperty]
    private bool _changeTimezone = true;

    [ObservableProperty]
    private bool _changeLocation = true;

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
        Initialize([deviceSerial], options, useIntegritySecurityPatch, isMultiple: false);
    }

    public void Initialize(
        IReadOnlyList<string> deviceSerials,
        DeviceChangeOptions options,
        bool useIntegritySecurityPatch = true,
        bool isMultiple = true)
    {
        ArgumentNullException.ThrowIfNull(deviceSerials);
        if (deviceSerials.Count == 0 || deviceSerials.Any(string.IsNullOrWhiteSpace))
            throw new ArgumentException("At least one device serial is required.", nameof(deviceSerials));
        ArgumentNullException.ThrowIfNull(options);

        _deviceSerials = deviceSerials
            .Where(serial => !string.IsNullOrWhiteSpace(serial))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        _deviceSerial = _deviceSerials[0];
        _ = isMultiple;
        ChangeAndroidId = options.ChangeAndroidId;
        ChangeMacAddress = options.ChangeMacAddress;
        UpdateIntegrity = options.UpdateIntegrity;
        ChangeTimezone = options.ChangeTimezone;
        ChangeLocation = options.ChangeLocation;
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
            _loadedPackages = await LoadPackagesForScopeAsync(scope.Scope, cancellationToken)
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

    private async Task<IReadOnlyList<string>> LoadPackagesForScopeAsync(
        PackageListScope scope,
        CancellationToken cancellationToken)
    {
        var packages = new HashSet<string>(StringComparer.Ordinal);
        bool packageQuerySucceeded = false;
        if (scope == PackageListScope.All)
        {
            foreach (string serial in _deviceSerials)
            {
                try
                {
                    packages.UnionWith(await _packageService
                        .GetInstalledPackagesAsync(serial, cancellationToken)
                        .ConfigureAwait(true));
                    packageQuerySucceeded = true;
                    break;
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    _logger.LogWarning(
                        exception,
                        "Failed to list installed packages for device {Serial}.",
                        serial);
                }
            }
        }

        foreach (string serial in _deviceSerials)
        {
            try
            {
                packages.UnionWith(await _packageService
                    .GetUserInstalledPackagesAsync(serial, cancellationToken)
                    .ConfigureAwait(true));
                packageQuerySucceeded = true;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                _logger.LogWarning(exception, "Failed to list user packages for device {Serial}.", serial);
            }
        }

        if (packages.Count == 0 && !packageQuerySucceeded)
            throw new InvalidOperationException("No package list could be loaded from the selected devices.");

        return packages.OrderBy(value => value, StringComparer.Ordinal).ToArray();
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
            UpdateIntegrity = UpdateIntegrity,
            ChangeTimezone = ChangeTimezone,
            ChangeLocation = ChangeLocation,
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
