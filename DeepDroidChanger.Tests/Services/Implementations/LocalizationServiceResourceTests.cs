using System.Reflection;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using DeepDroidChanger.Constants;

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
        string[] requiredKeys = typeof(DeviceLogResourceKeys)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.IsLiteral && field.FieldType == typeof(string))
            .Select(field => (string)field.GetRawConstantValue()!)
            .ToArray();

        CollectionAssert.AreEquivalent(requiredKeys, english.Keys.ToArray());
        CollectionAssert.AreEquivalent(requiredKeys, vietnamese.Keys.ToArray());
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
            "shell command..."
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
