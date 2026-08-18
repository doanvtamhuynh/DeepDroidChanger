using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace DeepDroidChanger.Tests.Services.Implementations;

[TestClass]
public sealed class LocalizationServiceResourceTests
{
    [TestMethod]
    public void Views_DoNotHardcodeUserFacingTextAttributes()
    {
        string projectRoot = Path.Combine(GetSolutionRoot(), "DeepDroidChanger");
        string[] files = Directory.GetFiles(Path.Combine(projectRoot, "Views"), "*.xaml", SearchOption.AllDirectories)
            .Append(Path.Combine(projectRoot, "MainWindow.xaml"))
            .ToArray();
        var hardcodedAttribute = new Regex(
            "(?:Text|Content|Header|Title|ToolTip)=\"(?!\\{|\\s*$)[^\"]*[A-Za-zÀ-ỹ][^\"]*\"",
            RegexOptions.CultureInvariant);
        string[] violations = files
            .Where(path => hardcodedAttribute.IsMatch(File.ReadAllText(path)))
            .Select(path => Path.GetRelativePath(projectRoot, path))
            .ToArray();

        Assert.IsEmpty(violations, $"Hardcoded user-facing XAML text found in: {string.Join(", ", violations)}");
    }

    [TestMethod]
    public void InteractiveControls_DefineTooltips()
    {
        string projectRoot = Path.Combine(GetSolutionRoot(), "DeepDroidChanger");
        string[] files = Directory.GetFiles(Path.Combine(projectRoot, "Views"), "*.xaml", SearchOption.AllDirectories)
            .Append(Path.Combine(projectRoot, "MainWindow.xaml"))
            .ToArray();
        HashSet<string> controlNames =
        [
            "Button",
            "ComboBox",
            "TextBox",
            "CheckBox",
            "RadioButton",
            "PasswordBox",
            "ListBox"
        ];
        string[] violations = files
            .SelectMany(path => XDocument.Load(path)
                .Descendants()
                .Where(element => controlNames.Contains(element.Name.LocalName))
                .Where(element => element.Attribute("ToolTip") == null)
                .Select(element => $"{Path.GetRelativePath(projectRoot, path)}:{element.Name.LocalName}"))
            .ToArray();

        Assert.IsEmpty(
            violations,
            $"Interactive controls without ToolTip: {string.Join(", ", violations)}");
    }

    [TestMethod]
    public void EnglishAndVietnameseDictionaries_ExposeTheSameNonEmptyKeys()
    {
        string stringsDirectory = Path.Combine(
            GetSolutionRoot(),
            "DeepDroidChanger",
            "Resources",
            "Strings",
            "Views");
        string[] files = Directory.GetFiles(stringsDirectory, "*.xaml", SearchOption.TopDirectoryOnly);
        var englishFiles = files.Where(path => !path.EndsWith(".vi.xaml", StringComparison.OrdinalIgnoreCase));
        var vietnameseFiles = files.Where(path => path.EndsWith(".vi.xaml", StringComparison.OrdinalIgnoreCase));

        IReadOnlyDictionary<string, string> english = LoadResources(englishFiles);
        IReadOnlyDictionary<string, string> vietnamese = LoadResources(vietnameseFiles);

        CollectionAssert.AreEquivalent(english.Keys.ToArray(), vietnamese.Keys.ToArray());
        Assert.IsFalse(english.Values.Any(string.IsNullOrWhiteSpace));
        Assert.IsFalse(vietnamese.Values.Any(string.IsNullOrWhiteSpace));
    }

    [TestMethod]
    public void RuntimeMessageKeys_AreDefinedInBothLanguages()
    {
        string stringsDirectory = Path.Combine(
            GetSolutionRoot(),
            "DeepDroidChanger",
            "Resources",
            "Strings",
            "Views");
        IReadOnlyDictionary<string, string> english = LoadResources(
            [Path.Combine(stringsDirectory, "RuntimeMessages.xaml")]);
        IReadOnlyDictionary<string, string> vietnamese = LoadResources(
            [Path.Combine(stringsDirectory, "RuntimeMessages.vi.xaml")]);
        string projectRoot = Path.Combine(GetSolutionRoot(), "DeepDroidChanger");
        var resourceKeyPattern = new Regex(
            "\"(?<key>Log_[A-Za-z0-9_]+)\"",
            RegexOptions.CultureInvariant);
        string[] requiredKeys = Directory
            .GetFiles(projectRoot, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .SelectMany(path => resourceKeyPattern.Matches(File.ReadAllText(path)))
            .Select(match => match.Groups["key"].Value)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        Assert.IsEmpty(
            requiredKeys.Except(english.Keys, StringComparer.Ordinal).ToArray(),
            "Runtime message keys are missing from the English resource dictionary.");
        Assert.IsEmpty(
            requiredKeys.Except(vietnamese.Keys, StringComparer.Ordinal).ToArray(),
            "Runtime message keys are missing from the Vietnamese resource dictionary.");
    }

    [TestMethod]
    public void EnglishAndVietnameseDictionaries_UseMatchingFormatPlaceholders()
    {
        string stringsDirectory = Path.Combine(
            GetSolutionRoot(),
            "DeepDroidChanger",
            "Resources",
            "Strings",
            "Views");
        string[] files = Directory.GetFiles(stringsDirectory, "*.xaml", SearchOption.TopDirectoryOnly);
        IReadOnlyDictionary<string, string> english = LoadResources(
            files.Where(path => !path.EndsWith(".vi.xaml", StringComparison.OrdinalIgnoreCase)));
        IReadOnlyDictionary<string, string> vietnamese = LoadResources(
            files.Where(path => path.EndsWith(".vi.xaml", StringComparison.OrdinalIgnoreCase)));

        foreach ((string key, string englishValue) in english)
        {
            string[] englishPlaceholders = GetFormatPlaceholders(englishValue);
            string[] vietnamesePlaceholders = GetFormatPlaceholders(vietnamese[key]);
            CollectionAssert.AreEquivalent(
                englishPlaceholders,
                vietnamesePlaceholders,
                $"Format placeholders differ for resource '{key}'.");
        }
    }

    [TestMethod]
    public void VietnameseDictionaries_DoNotContainKnownTranslationOrEncodingDefects()
    {
        string stringsDirectory = Path.Combine(
            GetSolutionRoot(),
            "DeepDroidChanger",
            "Resources",
            "Strings",
            "Views");
        IReadOnlyDictionary<string, string> vietnamese = LoadResources(
            Directory.GetFiles(stringsDirectory, "*.vi.xaml", SearchOption.TopDirectoryOnly));
        string[] forbiddenExactValues =
        [
            "Run",
            "Actived",
            "Not Actived",
            "Device Manager",
            "Actions",
            "INPUT TEXT",
            "shell command...",
            "Doi Location",
            "Doi Timezone",
            "Ap dung cho {0} thiet bi",
            "Ap dung Location cho cac thiet bi online da chon.",
            "Ap dung Timezone cho cac thiet bi online da chon."
        ];

        foreach ((string key, string value) in vietnamese)
        {
            Assert.IsFalse(
                forbiddenExactValues.Contains(value, StringComparer.OrdinalIgnoreCase),
                $"Vietnamese resource '{key}' still contains untranslated text '{value}'.");
            Assert.IsFalse(
                Regex.IsMatch(value, "Ã|Â|â€™|â€œ|â€|�", RegexOptions.CultureInvariant),
                $"Vietnamese resource '{key}' contains invalid text encoding.");
            Assert.IsFalse(
                Regex.IsMatch(value, "\\b(?:file|app|popup)\\b", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase),
                $"Vietnamese resource '{key}' contains an avoidable untranslated term in '{value}'.");
        }
    }

    [TestMethod]
    public void EnglishDictionaries_DoNotContainKnownGrammarDefects()
    {
        string stringsDirectory = Path.Combine(
            GetSolutionRoot(),
            "DeepDroidChanger",
            "Resources",
            "Strings",
            "Views");
        IReadOnlyDictionary<string, string> english = LoadResources(
            Directory.GetFiles(stringsDirectory, "*.xaml", SearchOption.TopDirectoryOnly)
                .Where(path => !path.EndsWith(".vi.xaml", StringComparison.OrdinalIgnoreCase)));
        string[] forbiddenValues =
        [
            "Change Info Single Devices",
            "Action Single Device",
            "Actived",
            "Not Actived",
            "Add devices success",
            "Delete device success",
            "Reboot device failed"
        ];

        foreach ((string key, string value) in english)
        {
            Assert.IsFalse(
                forbiddenValues.Contains(value, StringComparer.OrdinalIgnoreCase),
                $"English resource '{key}' contains invalid text '{value}'.");
        }
    }

    [TestMethod]
    public void ChangeSingleDeviceDictionaries_ExposeClearDeviceInfoFieldLabels()
    {
        string stringsDirectory = Path.Combine(
            GetSolutionRoot(),
            "DeepDroidChanger",
            "Resources",
            "Strings",
            "Views");
        IReadOnlyDictionary<string, string> english = LoadResources(
            [Path.Combine(stringsDirectory, "ChangeSingleDevice.xaml")]);
        IReadOnlyDictionary<string, string> vietnamese = LoadResources(
            [Path.Combine(stringsDirectory, "ChangeSingleDevice.vi.xaml")]);

        Assert.AreEqual("Hardware", english["ChangeSingleDevice_FieldHardware"]);
        Assert.AreEqual("Fingerprint", english["ChangeSingleDevice_FieldFingerprint"]);
        Assert.AreEqual("Wi-Fi MAC", english["ChangeSingleDevice_FieldMac"]);
        Assert.AreEqual("Phần cứng", vietnamese["ChangeSingleDevice_FieldHardware"]);
        Assert.AreEqual("Fingerprint", vietnamese["ChangeSingleDevice_FieldFingerprint"]);
        Assert.AreEqual("Wi-Fi MAC", vietnamese["ChangeSingleDevice_FieldMac"]);
    }

    [TestMethod]
    public void ChangeDeviceDictionaries_ShareEquivalentLabels()
    {
        string stringsDirectory = Path.Combine(
            GetSolutionRoot(),
            "DeepDroidChanger",
            "Resources",
            "Strings",
            "Views");
        (string Single, string Multiple)[] dictionaryPaths =
        [
            ("ChangeSingleDevice.xaml", "ChangeMultipleDevices.xaml"),
            ("ChangeSingleDevice.vi.xaml", "ChangeMultipleDevices.vi.xaml")
        ];
        (string Single, string Multiple)[] sharedKeys =
        [
            ("ChangeSingleDevice_StatusOnline", "ChangeMultipleDevices_StatusOnline"),
            ("ChangeSingleDevice_StatusOffline", "ChangeMultipleDevices_StatusOffline"),
            ("ChangeSingleDevice_StatusUnauthorized", "ChangeMultipleDevices_StatusUnauthorized"),
            ("ChangeSingleDevice_AddNewDevices", "ChangeMultipleDevices_AddDevices"),
            ("ChangeSingleDevice_NewDeviceCount", "ChangeMultipleDevices_NewDeviceCount"),
            ("ChangeSingleDevice_NotAvailable", "ChangeMultipleDevices_NotAvailable"),
            ("ChangeSingleDevice_ColumnSerial", "ChangeMultipleDevices_ColumnSerial"),
            ("ChangeSingleDevice_ColumnName", "ChangeMultipleDevices_ColumnName"),
            ("ChangeSingleDevice_ColumnType", "ChangeMultipleDevices_ColumnType"),
            ("ChangeSingleDevice_ColumnActive", "ChangeMultipleDevices_ColumnActive"),
            ("ChangeSingleDevice_ColumnStatus", "ChangeMultipleDevices_ColumnStatus"),
            ("ChangeSingleDevice_ColumnProcess", "ChangeMultipleDevices_ColumnProcess"),
            ("ChangeSingleDevice_DeviceInfoHeader", "ChangeMultipleDevices_DeviceInfoHeader"),
            ("ChangeSingleDevice_DeviceConfigHeader", "ChangeMultipleDevices_ChangeConfigurationHeader"),
            ("ChangeSingleDevice_FieldName", "ChangeMultipleDevices_FieldName"),
            ("ChangeSingleDevice_FieldImei", "ChangeMultipleDevices_FieldImei"),
            ("ChangeSingleDevice_FieldHardware", "ChangeMultipleDevices_FieldHardware"),
            ("ChangeSingleDevice_FieldOperator", "ChangeMultipleDevices_FieldOperator"),
            ("ChangeSingleDevice_FieldFingerprint", "ChangeMultipleDevices_FieldFingerprint"),
            ("ChangeSingleDevice_FieldPhoneNumber", "ChangeMultipleDevices_FieldPhoneNumber"),
            ("ChangeSingleDevice_FieldOsVersion", "ChangeMultipleDevices_FieldOsVersion"),
            ("ChangeSingleDevice_FieldIccid", "ChangeMultipleDevices_FieldIccid"),
            ("ChangeSingleDevice_FieldImsi", "ChangeMultipleDevices_FieldImsi"),
            ("ChangeSingleDevice_FieldSerial", "ChangeMultipleDevices_FieldSerial"),
            ("ChangeSingleDevice_FieldMac", "ChangeMultipleDevices_FieldMac"),
            ("ChangeSingleDevice_FieldBrand", "ChangeMultipleDevices_FieldBrand"),
            ("ChangeSingleDevice_FieldOsVersion", "ChangeMultipleDevices_FieldAndroidVersion"),
            ("ChangeSingleDevice_FieldModel", "ChangeMultipleDevices_FieldModel"),
            ("ChangeSingleDevice_FieldCountry", "ChangeMultipleDevices_FieldCountry"),
            ("ChangeSingleDevice_FieldCarrier", "ChangeMultipleDevices_FieldCarrier"),
            ("ChangeSingleDevice_OptionChangeSim", "ChangeMultipleDevices_OptionChangeSim"),
            ("ChangeSingleDevice_OptionDefaultChangeMode", "ChangeMultipleDevices_OptionDefaultMode"),
            ("ChangeSingleDevice_ActionAdvancedChangeConfig", "ChangeMultipleDevices_AdvancedConfig"),
            ("ChangeSingleDevice_FilterLabel", "ChangeMultipleDevices_FilterLabel"),
            ("ChangeSingleDevice_FilterAll", "ChangeMultipleDevices_FilterAll"),
            ("ChangeSingleDevice_FilterOnline", "ChangeMultipleDevices_FilterOnline"),
            ("ChangeSingleDevice_FilterOffline", "ChangeMultipleDevices_FilterOffline"),
            ("ChangeSingleDevice_FilterActive", "ChangeMultipleDevices_FilterActive"),
            ("ChangeSingleDevice_FilterInactive", "ChangeMultipleDevices_FilterInactive")
        ];

        foreach ((string singlePath, string multiplePath) in dictionaryPaths)
        {
            IReadOnlyDictionary<string, string> single = LoadResources(
                [Path.Combine(stringsDirectory, singlePath)]);
            IReadOnlyDictionary<string, string> multiple = LoadResources(
                [Path.Combine(stringsDirectory, multiplePath)]);

            foreach ((string singleKey, string multipleKey) in sharedKeys)
            {
                Assert.AreEqual(
                    single[singleKey],
                    multiple[multipleKey],
                    $"{singlePath}: '{singleKey}' and '{multipleKey}' should use the same text.");
            }
        }

        IReadOnlyDictionary<string, string> englishSingle = LoadResources(
            [Path.Combine(stringsDirectory, "ChangeSingleDevice.xaml")]);
        IReadOnlyDictionary<string, string> englishMultiple = LoadResources(
            [Path.Combine(stringsDirectory, "ChangeMultipleDevices.xaml")]);
        IReadOnlyDictionary<string, string> vietnameseSingle = LoadResources(
            [Path.Combine(stringsDirectory, "ChangeSingleDevice.vi.xaml")]);
        IReadOnlyDictionary<string, string> vietnameseMultiple = LoadResources(
            [Path.Combine(stringsDirectory, "ChangeMultipleDevices.vi.xaml")]);

        Assert.AreEqual("Select", englishSingle["ChangeSingleDevice_ColumnSelected"]);
        Assert.AreEqual("Select", englishMultiple["ChangeMultipleDevices_ColumnSelected"]);
        Assert.AreEqual("Chọn", vietnameseSingle["ChangeSingleDevice_ColumnSelected"]);
        Assert.AreEqual("Chọn", vietnameseMultiple["ChangeMultipleDevices_ColumnSelected"]);
    }

    private static IReadOnlyDictionary<string, string> LoadResources(IEnumerable<string> paths)
    {
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
        var resources = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string path in paths)
        {
            XDocument document = XDocument.Load(path);
            foreach (XElement element in document.Descendants())
            {
                XAttribute? key = element.Attribute(xaml + "Key");
                if (key == null)
                    continue;

                resources.Add(key.Value, element.Value);
            }
        }

        return resources;
    }

    private static string[] GetFormatPlaceholders(string value)
    {
        return Regex.Matches(value, "\\{\\d+(?:[^}]*)?\\}", RegexOptions.CultureInvariant)
            .Select(match => match.Value)
            .OrderBy(placeholder => placeholder, StringComparer.Ordinal)
            .ToArray();
    }

    private static string GetSolutionRoot()
    {
        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
    }
}
