using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace DeepDroidChanger.Tests.Architecture;

[TestClass]
public sealed class ArchitectureRuleTests
{
    [TestMethod]
    public void DeviceManagerFeature_UsesCanonicalStructureAndResourceOwnership()
    {
        string projectRoot = GetProjectRoot();
        string solutionRoot = GetSolutionRoot();
        string canonicalView = Path.Combine(projectRoot, "Views", "DeviceManager", "DeviceManagerView.xaml");
        string canonicalViewModel = Path.Combine(projectRoot, "ViewModels", "DeviceManagerViewModel.cs");
        string canonicalTheme = Path.Combine(projectRoot, "Resources", "Themes", "DeviceManager.xaml");
        string canonicalStrings = Path.Combine(projectRoot, "Resources", "Strings", "Views", "DeviceManager.xaml");
        string canonicalVietnameseStrings = Path.Combine(projectRoot, "Resources", "Strings", "Views", "DeviceManager.vi.xaml");
        string canonicalTests = Path.Combine(
            solutionRoot,
            "DeepDroidChanger.Tests",
            "ViewModels",
            "DeviceManagerViewModelLifecycleTests.cs");

        Assert.IsTrue(File.Exists(canonicalView));
        Assert.IsTrue(File.Exists(canonicalViewModel));
        Assert.IsTrue(File.Exists(canonicalTheme));
        Assert.IsTrue(File.Exists(canonicalStrings));
        Assert.IsTrue(File.Exists(canonicalVietnameseStrings));
        Assert.IsTrue(File.Exists(canonicalTests));
        Assert.IsFalse(Directory.Exists(Path.Combine(projectRoot, "Views", "Devices")));
        Assert.IsFalse(File.Exists(Path.Combine(projectRoot, "ViewModels", "DevicesViewModel.cs")));
        Assert.IsFalse(File.Exists(Path.Combine(projectRoot, "Resources", "Themes", "Devices.xaml")));

        AssertResourceKeysUsePrefix(canonicalStrings, "DeviceManager_");
        AssertResourceKeysUsePrefix(canonicalVietnameseStrings, "DeviceManager_");
        AssertLocalizedResourceKeysMatch(canonicalStrings, canonicalVietnameseStrings);
        AssertLocalizedResourceKeysMatch(
            Path.Combine(projectRoot, "Resources", "Strings", "Views", "RandomDeviceInfo.xaml"),
            Path.Combine(projectRoot, "Resources", "Strings", "Views", "RandomDeviceInfo.vi.xaml"));
        string englishStrings = File.ReadAllText(canonicalStrings);
        string vietnameseStrings = File.ReadAllText(canonicalVietnameseStrings);
        StringAssert.Contains(englishStrings, "DeviceManager_FieldOsVersion\">OS Version");
        StringAssert.Contains(vietnameseStrings, "DeviceManager_FieldOsVersion\">Phiên bản OS");
        StringAssert.Contains(
            englishStrings,
            "DeviceManager_OptionDefaultChangeMode\">Change and Wipe default");
        StringAssert.Contains(
            vietnameseStrings,
            "DeviceManager_OptionDefaultChangeMode\">Đổi và Wipe mặc định");
        Assert.DoesNotContain("DeviceManager_FieldAndroidVersion", englishStrings, StringComparison.Ordinal);
        Assert.DoesNotContain("DeviceManager_FieldAndroidVersion", vietnameseStrings, StringComparison.Ordinal);
        Assert.DoesNotContain(">Android Version<", englishStrings, StringComparison.Ordinal);
        Assert.DoesNotContain(">Phiên bản Android<", vietnameseStrings, StringComparison.Ordinal);
        AssertResourceKeysUsePrefix(
            Path.Combine(projectRoot, "Resources", "Strings", "Views", "AddDevices.xaml"),
            "AddDevices_");
        AssertResourceKeysUsePrefix(
            Path.Combine(projectRoot, "Resources", "Strings", "Views", "AddDevices.vi.xaml"),
            "AddDevices_");

        string deviceManagerView = File.ReadAllText(canonicalView);
        Assert.DoesNotContain(
            "StaticResource AddDevice",
            deviceManagerView,
            StringComparison.Ordinal,
            "DeviceManagerView must not depend on AddDevices-owned styles.");
    }

    [TestMethod]
    public void DomainModels_AreStoredInDesignatedDesignFolders()
    {
        string modelsDirectory = Path.Combine(GetProjectRoot(), "Models");

        AssertFilesInFolder(
            modelsDirectory,
            "AdbServices",
            "AdbDevice.cs",
            "AdbDeviceStatus.cs",
            "DeviceViewerStreamBounds.cs",
            "InstallPackageOptions.cs",
            "InstallPackageResult.cs",
            "Integrity.cs",
            "ObbFileInfo.cs",
            "SocksProxyCheckResult.cs",
            "XapkPackageInfo.cs");
        AssertFilesInFolder(
            modelsDirectory,
            "DeviceInfo",
            "DeviceInfoApiDevice.cs",
            "DeviceInfoApiOptions.cs",
            "RandomDeviceRequest.cs",
            "RandomDeviceResult.cs",
            "RandomDeviceSelection.cs");
    }

    [TestMethod]
    public void DomainServices_AreStoredInDesignatedDesignFolders()
    {
        string servicesDirectory = Path.Combine(GetProjectRoot(), "Services");
        string interfacesDirectory = Path.Combine(servicesDirectory, "Interfaces");
        string implementationsDirectory = Path.Combine(servicesDirectory, "Implementations");

        AssertFilesInFolder(
            interfacesDirectory,
            "DeviceInfo",
            "ICarrierDataService.cs",
            "IDeviceRandomApiService.cs",
            "IDeviceRandomProfileService.cs",
            "IRandomDeviceService.cs");
        AssertFilesInFolder(
            implementationsDirectory,
            "DeviceInfo",
            "CarrierDataService.cs",
            "DeviceRandomApiException.cs",
            "DeviceRandomApiService.cs",
            "DeviceRandomProfileService.cs",
            "RandomDeviceService.cs");
    }

    [TestMethod]
    public void ServiceInterfacesAndImplementations_UseMatchingDomainFolders()
    {
        string servicesDirectory = Path.Combine(GetProjectRoot(), "Services");
        string interfacesDirectory = Path.Combine(servicesDirectory, "Interfaces");
        string implementationsDirectory = Path.Combine(servicesDirectory, "Implementations");

        foreach (string interfaceFile in Directory.GetFiles(interfacesDirectory, "I*Service.cs", SearchOption.AllDirectories))
        {
            string implementationName = Path.GetFileName(interfaceFile)[1..];
            string[] implementationFiles = Directory.GetFiles(
                implementationsDirectory,
                implementationName,
                SearchOption.AllDirectories);
            Assert.AreEqual(1, implementationFiles.Length, $"Implementation mismatch for '{interfaceFile}'.");

            string interfaceDomain = GetRelativeDirectory(interfacesDirectory, interfaceFile);
            string implementationDomain = GetRelativeDirectory(implementationsDirectory, implementationFiles[0]);
            Assert.AreEqual(interfaceDomain, implementationDomain, $"Domain folder mismatch for '{interfaceFile}'.");
        }
    }

    [TestMethod]
    public void ServiceTests_MirrorImplementationDomainFolders()
    {
        string solutionRoot = GetSolutionRoot();
        string testsDirectory = Path.Combine(solutionRoot, "DeepDroidChanger.Tests", "Services", "Implementations");
        string implementationsDirectory = Path.Combine(GetProjectRoot(), "Services", "Implementations");

        foreach (string testFile in Directory.GetFiles(testsDirectory, "*Tests.cs", SearchOption.AllDirectories))
        {
            string testName = Path.GetFileNameWithoutExtension(testFile);
            string implementationName = testName == "LocalizationServiceResourceTests"
                ? "LocalizationService.cs"
                : string.Concat(testName[..^"Tests".Length], ".cs");
            string[] implementationFiles = Directory.GetFiles(
                implementationsDirectory,
                implementationName,
                SearchOption.AllDirectories);
            Assert.AreEqual(1, implementationFiles.Length, $"Production file mismatch for '{testFile}'.");

            string testDomain = GetRelativeDirectory(testsDirectory, testFile);
            string implementationDomain = GetRelativeDirectory(implementationsDirectory, implementationFiles[0]);
            Assert.AreEqual(implementationDomain, testDomain, $"Test folder does not mirror '{implementationFiles[0]}'.");
        }
    }

    [TestMethod]
    public void AuthenticationProject_UsesCanonicalStructureAndOneWayDependencies()
    {
        string solutionRoot = GetSolutionRoot();
        string authenticationRoot = Path.Combine(
            solutionRoot,
            "DeepDroidChanger.Authentication");
        string authenticationProject = Path.Combine(
            authenticationRoot,
            "DeepDroidChanger.Authentication.csproj");
        string applicationProject = Path.Combine(
            solutionRoot,
            "DeepDroidChanger",
            "DeepDroidChanger.csproj");
        string testProject = Path.Combine(
            solutionRoot,
            "DeepDroidChanger.Tests",
            "DeepDroidChanger.Tests.csproj");

        AssertFilesInFolder(
            authenticationRoot,
            "Constants",
            "AccountStoreConstants.cs",
            "AuthenticationConstants.cs");
        AssertFilesInFolder(
            authenticationRoot,
            "Models",
            "AccountAuthenticationResult.cs",
            "AccountLoginRequest.cs",
            "AccountSession.cs",
            "AccountStoreOptions.cs",
            "AuthenticationOptions.cs",
            "IdentityProviderAuthenticationResult.cs");
        AssertFilesInFolder(
            Path.Combine(authenticationRoot, "Services"),
            "Interfaces",
            "IAccountAuthenticationService.cs",
            "IAccountStoreService.cs",
            "IAuthenticationSessionService.cs",
            "IIdentityProviderClient.cs");
        AssertFilesInFolder(
            Path.Combine(authenticationRoot, "Services"),
            "Implementations",
            "AccountAuthenticationService.cs",
            "AccountStoreService.cs",
            "AuthenticationSessionService.cs",
            "CognitoIdentityProviderClient.cs");

        XDocument authenticationDocument = XDocument.Load(authenticationProject);
        XDocument applicationDocument = XDocument.Load(applicationProject);
        XDocument testDocument = XDocument.Load(testProject);
        string[] authenticationReferences = GetProjectReferences(authenticationDocument);
        string[] applicationReferences = GetProjectReferences(applicationDocument);
        string[] testReferences = GetProjectReferences(testDocument);

        Assert.IsEmpty(authenticationReferences);
        Assert.IsFalse(authenticationDocument.Descendants()
            .Any(element => element.Name.LocalName == "UseWPF"
                && string.Equals(element.Value, "true", StringComparison.OrdinalIgnoreCase)));
        Assert.IsFalse(authenticationDocument.Descendants()
            .Any(element => element.Name.LocalName == "InternalsVisibleTo"));
        Assert.Contains(
            @"..\DeepDroidChanger.Authentication\DeepDroidChanger.Authentication.csproj",
            applicationReferences);
        Assert.Contains(
            @"..\DeepDroidChanger.Authentication\DeepDroidChanger.Authentication.csproj",
            testReferences);
        Assert.Contains(
            @"..\DeepDroidChanger\DeepDroidChanger.csproj",
            testReferences);

        string applicationProjectSource = File.ReadAllText(applicationProject);
        string authenticationProjectSource = File.ReadAllText(authenticationProject);
        Assert.DoesNotContain(
            "Amazon.Extensions.CognitoAuthentication",
            applicationProjectSource,
            StringComparison.Ordinal);
        Assert.Contains(
            "Amazon.Extensions.CognitoAuthentication",
            authenticationProjectSource,
            StringComparison.Ordinal);

        string coverageSettings = File.ReadAllText(Path.Combine(
            solutionRoot,
            "DeepDroidChanger.Tests",
            "coverage.settings.xml"));
        string coverageVerification = File.ReadAllText(Path.Combine(
            solutionRoot,
            "DeepDroidChanger.Tests",
            "verify-coverage.ps1"));
        Assert.Contains(
            @"DeepDroidChanger(?:\.Authentication)?\.dll$",
            coverageSettings,
            StringComparison.Ordinal);
        Assert.Contains(
            @"DeepDroidChanger\.Authentication\.Internal",
            coverageSettings,
            StringComparison.Ordinal);
        Assert.Contains(
            "DeepDroidChanger.Authentication.Internal.AccountAuthenticationService",
            coverageVerification,
            StringComparison.Ordinal);

        string[] authenticationSourceFiles = Directory.GetFiles(
            authenticationRoot,
            "*.cs",
            SearchOption.AllDirectories);
        string[] forbiddenNamespaces =
        [
            "DeepDroidChanger.Models",
            "DeepDroidChanger.Services",
            "DeepDroidChanger.ViewModels",
            "DeepDroidChanger.Views"
        ];
        Assert.IsFalse(authenticationSourceFiles.Any(path =>
            forbiddenNamespaces.Any(forbiddenNamespace =>
                File.ReadAllText(path).Contains(
                    forbiddenNamespace,
                    StringComparison.Ordinal))));
        Assert.IsFalse(authenticationSourceFiles.Any(path =>
            File.ReadAllText(path).Contains(
                "InternalsVisibleTo",
                StringComparison.Ordinal)));
        string constantsDirectory = Path.Combine(authenticationRoot, "Constants");
        Assert.IsTrue(Directory.GetFiles(
                constantsDirectory,
                "*.cs",
                SearchOption.TopDirectoryOnly)
            .All(path => Path.GetFileNameWithoutExtension(path)
                .EndsWith("Constants", StringComparison.Ordinal)));
        Assert.IsFalse(authenticationSourceFiles.Any(path =>
            File.ReadAllText(path).Contains(
                "appsync-api",
                StringComparison.OrdinalIgnoreCase)));

        string deviceInfoApiInterface = File.ReadAllText(Path.Combine(
            GetProjectRoot(),
            "Services",
            "Interfaces",
            "DeviceInfo",
            "IDeviceRandomApiService.cs"));
        string deviceInfoProfileInterface = File.ReadAllText(Path.Combine(
            GetProjectRoot(),
            "Services",
            "Interfaces",
            "DeviceInfo",
            "IDeviceRandomProfileService.cs"));
        Assert.DoesNotContain(
            "AccountSession",
            deviceInfoApiInterface,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "DeepDroidChanger.Authentication",
            deviceInfoApiInterface,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "AccountSession",
            deviceInfoProfileInterface,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "DeepDroidChanger.Authentication",
            deviceInfoProfileInterface,
            StringComparison.Ordinal);

        string implementationsDirectory = Path.Combine(
            authenticationRoot,
            "Services",
            "Implementations");
        foreach (string implementationFile in Directory.GetFiles(
                     implementationsDirectory,
                     "*.cs",
                     SearchOption.TopDirectoryOnly))
        {
            Assert.Contains(
                "internal sealed class",
                File.ReadAllText(implementationFile),
                StringComparison.Ordinal);
        }
    }

    [TestMethod]
    public void Models_ContainDataOnlyAndDoNotReferenceUpperLayers()
    {
        string modelsDirectory = Path.Combine(GetProjectRoot(), "Models");
        foreach (string file in Directory.GetFiles(modelsDirectory, "*.cs", SearchOption.AllDirectories))
        {
            string source = File.ReadAllText(file);
            Assert.IsFalse(source.Contains("DeepDroidChanger.Services", StringComparison.Ordinal), file);
            Assert.IsFalse(source.Contains("DeepDroidChanger.ViewModels", StringComparison.Ordinal), file);
            Assert.IsFalse(
                Regex.IsMatch(source, "=>|\\b(?:if|switch|for|foreach|while|try|catch)\\b", RegexOptions.CultureInvariant),
                $"Model contains processing logic: '{file}'.");
        }
    }

    [TestMethod]
    public void Constants_ContainOnlyApprovedCatalogFiles()
    {
        string constantsDirectory = Path.Combine(GetProjectRoot(), "Constants");
        string[] expectedFiles =
        [
            "AssetConstants.cs",
            "DeviceSettingsInfoConstants.cs",
            "PropertyConstants.cs",
            "UrlConstants.cs"
        ];
        string[] actualFiles = Directory
            .GetFiles(constantsDirectory, "*.cs", SearchOption.AllDirectories)
            .Select(Path.GetFileName)
            .Order(StringComparer.Ordinal)
            .ToArray()!;

        CollectionAssert.AreEqual(expectedFiles, actualFiles);
    }

    [TestMethod]
    public void Constants_ContainNoProcessingMethods()
    {
        string constantsDirectory = Path.Combine(GetProjectRoot(), "Constants");
        var methodPattern = new Regex(
            "\\b(?:public|internal|private|protected)\\s+static\\s+(?!class\\b|readonly\\b)[^=;{}]+\\(",
            RegexOptions.CultureInvariant);

        foreach (string file in Directory.GetFiles(constantsDirectory, "*.cs", SearchOption.AllDirectories))
        {
            string source = File.ReadAllText(file);
            Assert.IsFalse(methodPattern.IsMatch(source), $"Constants file contains processing logic: '{file}'.");
        }
    }

    [TestMethod]
    public void Constants_ExposeOnlyStringConstants()
    {
        string constantsDirectory = Path.Combine(GetProjectRoot(), "Constants");
        var nonStringConstantPattern = new Regex(
            "\\bpublic\\s+const\\s+(?!string\\b)",
            RegexOptions.CultureInvariant);

        foreach (string file in Directory.GetFiles(constantsDirectory, "*.cs", SearchOption.AllDirectories))
        {
            string source = File.ReadAllText(file);
            Assert.IsFalse(
                nonStringConstantPattern.IsMatch(source),
                $"Constants catalog exposes a non-string value: '{file}'.");
        }
    }

    [TestMethod]
    public void AssetConstants_ContainOnlyApprovedSharedAssetAndRuntimeCatalogs()
    {
        string path = Path.Combine(GetProjectRoot(), "Constants", "AssetConstants.cs");
        string source = File.ReadAllText(path);
        string[] expectedNestedCatalogs =
        [
            "Data",
            "Icons",
            "Localization",
            "RuntimeData",
            "Themes",
            "Tools"
        ];
        string[] actualNestedCatalogs = Regex
            .Matches(source, "\\bpublic\\s+static\\s+class\\s+(?<name>[A-Za-z0-9_]+)")
            .Select(match => match.Groups["name"].Value)
            .Where(name => !string.Equals(name, "AssetConstants", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)
            .ToArray();

        CollectionAssert.AreEqual(expectedNestedCatalogs, actualNestedCatalogs);
    }

    [TestMethod]
    public void DeviceSettingsInfoConstants_ContainOnlyNamespacesAndSettingKeys()
    {
        string path = Path.Combine(GetProjectRoot(), "Constants", "DeviceSettingsInfoConstants.cs");
        string source = File.ReadAllText(path);
        string[] expectedNames =
        [
            "AndroidId",
            "BluetoothAddress",
            "BluetoothAddressValid",
            "BluetoothName",
            "DeviceName",
            "GlobalNamespace",
            "RandomMac",
            "ScreenTimeout",
            "SecureNamespace",
            "SystemNamespace",
            "WifiP2pDeviceName"
        ];
        string[] actualNames = Regex
            .Matches(source, "\\bpublic\\s+const\\s+string\\s+(?<name>[A-Za-z0-9_]+)\\s*=")
            .Select(match => match.Groups["name"].Value)
            .Order(StringComparer.Ordinal)
            .ToArray();

        CollectionAssert.AreEqual(expectedNames, actualNames);
    }

    [TestMethod]
    public void Constants_ExcludeOperationalValuesArgumentsAndCommands()
    {
        string constantsDirectory = Path.Combine(GetProjectRoot(), "Constants");
        string[] memberNames = Directory
            .GetFiles(constantsDirectory, "*.cs", SearchOption.AllDirectories)
            .SelectMany(path => Regex.Matches(
                File.ReadAllText(path),
                "\\b(?:public|private|internal)\\s+const\\s+string\\s+(?<name>[A-Za-z0-9_]+)\\s*="))
            .Select(match => match.Groups["name"].Value)
            .Where(name => !string.Equals(name, "ChangeOptionsConfigFileName", StringComparison.Ordinal))
            .ToArray();
        string[] forbiddenMemberTerms =
        [
            "Argument",
            "Command",
            "DisabledValue",
            "EnabledValue",
            "FailureCode",
            "Filter",
            "KeyEvent",
            "Option",
            "ResourceKey",
            "RootUserId",
            "TimeoutMilliseconds",
            "TimeoutSeconds"
        ];

        foreach (string term in forbiddenMemberTerms)
        {
            Assert.IsFalse(
                memberNames.Any(name => name.Contains(term, StringComparison.Ordinal)),
                $"Constants catalogs contain forbidden operational member term '{term}'.");
        }
    }

    [TestMethod]
    public void ViewCodeBehind_DoesNotReferenceServiceLayer()
    {
        string projectRoot = GetProjectRoot();
        string[] viewFiles = Directory.GetFiles(Path.Combine(projectRoot, "Views"), "*.xaml.cs", SearchOption.AllDirectories)
            .Append(Path.Combine(projectRoot, "MainWindow.xaml.cs"))
            .ToArray();

        foreach (string file in viewFiles)
        {
            string source = File.ReadAllText(file);
            Assert.IsFalse(
                source.Contains("DeepDroidChanger.Services", StringComparison.Ordinal),
                $"View code-behind references the service layer: '{file}'.");
        }
    }

    [TestMethod]
    public void ResourcesAndAssets_ContainOnlyAllowedFileTypes()
    {
        string projectRoot = GetProjectRoot();
        string[] invalidResourceFiles = Directory.GetFiles(
                Path.Combine(projectRoot, "Resources"),
                "*",
                SearchOption.AllDirectories)
            .Where(path => !path.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase))
            .Where(path => !path.EndsWith($"{Path.DirectorySeparatorChar}ThemeManager.cs", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        string[] xamlAssets = Directory.GetFiles(
            Path.Combine(projectRoot, "Assets"),
            "*.xaml",
            SearchOption.AllDirectories);

        Assert.IsEmpty(invalidResourceFiles);
        Assert.IsEmpty(xamlAssets);
    }

    [TestMethod]
    public void ConfirmationServices_UseSharedThemedDialogWithoutNativeMessageBox()
    {
        string projectRoot = GetProjectRoot();
        string[] requiredPaths =
        [
            Path.Combine(projectRoot, "Views", "Dialogs", "Confirmation", "ConfirmationDialog.xaml"),
            Path.Combine(projectRoot, "Views", "Dialogs", "Confirmation", "ConfirmationDialog.xaml.cs"),
            Path.Combine(projectRoot, "ViewModels", "Dialogs", "ConfirmationDialogViewModel.cs"),
            Path.Combine(projectRoot, "Services", "Interfaces", "DialogServices", "IConfirmationDialogService.cs"),
            Path.Combine(projectRoot, "Services", "Implementations", "DialogServices", "ConfirmationDialogService.cs")
        ];
        Assert.IsTrue(requiredPaths.All(File.Exists));

        string[] obsoletePaths =
        [
            Path.Combine(projectRoot, "Views", "Dialogs", "ChangeDeviceConfirmation", "ChangeDeviceConfirmationDialog.xaml"),
            Path.Combine(projectRoot, "Views", "Dialogs", "ChangeDeviceConfirmation", "ChangeDeviceConfirmationDialog.xaml.cs"),
            Path.Combine(projectRoot, "Views", "Dialogs", "DeleteDeviceConfirmation", "DeleteDeviceConfirmationDialog.xaml"),
            Path.Combine(projectRoot, "Views", "Dialogs", "DeleteDeviceConfirmation", "DeleteDeviceConfirmationDialog.xaml.cs"),
            Path.Combine(projectRoot, "ViewModels", "Dialogs", "ChangeDeviceConfirmationViewModel.cs"),
            Path.Combine(projectRoot, "ViewModels", "Dialogs", "DeleteDeviceConfirmationViewModel.cs"),
            Path.Combine(projectRoot, "Resources", "Themes", "DeleteDeviceConfirmation.xaml"),
            Path.Combine(projectRoot, "Services", "Interfaces", "DialogServices", "IMessageBoxService.cs"),
            Path.Combine(projectRoot, "Services", "Implementations", "DialogServices", "MessageBoxService.cs")
        ];
        Assert.IsTrue(obsoletePaths.All(path => !File.Exists(path)));

        string dialogSource = File.ReadAllText(requiredPaths[0]);
        Assert.Contains("Kind=\"Alert\"", dialogSource, StringComparison.Ordinal);
        Assert.Contains("Kind=\"{Binding IconKind}\"", dialogSource, StringComparison.Ordinal);
        Assert.Contains("Brush.Warning", dialogSource, StringComparison.Ordinal);
        Assert.Contains("Content=\"{Binding CancelButtonText}\"", dialogSource, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding ConfirmButtonText}\"", dialogSource, StringComparison.Ordinal);
        Assert.Contains("IsCancel=\"True\"", dialogSource, StringComparison.Ordinal);
        Assert.Contains("IsDefault=\"True\"", dialogSource, StringComparison.Ordinal);

        string stringSource = File.ReadAllText(Path.Combine(
            projectRoot,
            "Resources",
            "Strings",
            "Views",
            "ConfirmationDialog.xaml"));
        Assert.Contains("ConfirmationDialog_NoButton", stringSource, StringComparison.Ordinal);
        Assert.Contains("ConfirmationDialog_YesButton", stringSource, StringComparison.Ordinal);

        string[] sourceFiles = Directory.GetFiles(projectRoot, "*.cs", SearchOption.AllDirectories);
        string nativeMessageBoxUsages = string.Join(
            Environment.NewLine,
            sourceFiles.Select(File.ReadAllText).Where(source => source.Contains("MessageBox.Show", StringComparison.Ordinal)));
        Assert.IsEmpty(nativeMessageBoxUsages);
    }

    private static string GetProjectRoot()
    {
        return Path.Combine(GetSolutionRoot(), "DeepDroidChanger");
    }

    private static string[] GetProjectReferences(XDocument document)
    {
        return document
            .Descendants()
            .Where(element => element.Name.LocalName == "ProjectReference")
            .Select(element => element.Attribute("Include")?.Value)
            .Where(value => value != null)
            .Cast<string>()
            .ToArray();
    }

    private static void AssertFilesInFolder(string root, string folder, params string[] fileNames)
    {
        foreach (string fileName in fileNames)
        {
            string expectedPath = Path.Combine(root, folder, fileName);
            string legacyPath = Path.Combine(root, fileName);
            Assert.IsTrue(File.Exists(expectedPath), $"Expected design path is missing: '{expectedPath}'.");
            Assert.IsFalse(File.Exists(legacyPath), $"Legacy flat path still exists: '{legacyPath}'.");
        }
    }

    private static void AssertResourceKeysUsePrefix(string resourcePath, string prefix)
    {
        string source = File.ReadAllText(resourcePath);
        string[] keys = Regex.Matches(source, "x:Key=\"([^\"]+)\"")
            .Select(match => match.Groups[1].Value)
            .ToArray();
        Assert.IsNotEmpty(keys, $"No resource keys found in '{resourcePath}'.");
        Assert.IsTrue(
            keys.All(key => key.StartsWith(prefix, StringComparison.Ordinal)),
            $"Resource dictionary '{resourcePath}' contains a key outside the '{prefix}' namespace.");
    }

    private static void AssertLocalizedResourceKeysMatch(string englishPath, string vietnamesePath)
    {
        string[] englishKeys = Regex.Matches(File.ReadAllText(englishPath), "x:Key=\"([^\"]+)\"")
            .Select(match => match.Groups[1].Value)
            .OrderBy(key => key, StringComparer.Ordinal)
            .ToArray();
        string[] vietnameseKeys = Regex.Matches(File.ReadAllText(vietnamesePath), "x:Key=\"([^\"]+)\"")
            .Select(match => match.Groups[1].Value)
            .OrderBy(key => key, StringComparer.Ordinal)
            .ToArray();

        CollectionAssert.AreEqual(englishKeys, vietnameseKeys, Path.GetFileName(englishPath));
    }

    private static string GetRelativeDirectory(string root, string file)
    {
        string relativeDirectory = Path.GetDirectoryName(Path.GetRelativePath(root, file)) ?? string.Empty;
        return relativeDirectory == "." ? string.Empty : relativeDirectory;
    }

    private static string GetSolutionRoot()
    {
        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
    }
}
