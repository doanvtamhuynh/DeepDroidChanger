using System.Text.RegularExpressions;

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
            "Authentication",
            "AccountAuthenticationResult.cs",
            "AccountLoginRequest.cs",
            "AccountSession.cs",
            "AccountSettings.cs");
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
            "Authentication",
            "IAccountAuthenticationService.cs",
            "IAccountStoreService.cs",
            "IDeviceSessionService.cs");
        AssertFilesInFolder(
            implementationsDirectory,
            "Authentication",
            "AccountAuthenticationService.cs",
            "AccountStoreService.cs",
            "DeviceSessionService.cs");
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

    private static string GetProjectRoot()
    {
        return Path.Combine(GetSolutionRoot(), "DeepDroidChanger");
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
