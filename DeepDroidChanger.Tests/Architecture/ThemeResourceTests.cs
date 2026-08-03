using System.Text.RegularExpressions;

namespace DeepDroidChanger.Tests.Architecture;

[TestClass]
public sealed partial class ThemeResourceTests
{
    [TestMethod]
    public void LightAndDarkPalettes_DefineMatchingColorAndBrushKeys()
    {
        string themesRoot = Path.Combine(GetSolutionRoot(), "DeepDroidChanger", "Resources", "Themes");
        string[] lightKeys = GetPaletteKeys(Path.Combine(themesRoot, "Theme.Light.xaml"));
        string[] darkKeys = GetPaletteKeys(Path.Combine(themesRoot, "Theme.Dark.xaml"));

        CollectionAssert.AreEquivalent(lightKeys, darkKeys);
    }

    [TestMethod]
    public void DynamicResources_AllResolveToProjectKeys()
    {
        string projectRoot = Path.Combine(GetSolutionRoot(), "DeepDroidChanger");
        string[] files = Directory.GetFiles(projectRoot, "*.xaml", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var definedKeys = files
            .SelectMany(path => KeyDefinitionRegex().Matches(File.ReadAllText(path)).Select(match => match.Groups[1].Value))
            .ToHashSet(StringComparer.Ordinal);
        var usedKeys = files
            .SelectMany(path => DynamicResourceRegex().Matches(File.ReadAllText(path)).Select(match => match.Groups[1].Value.Trim()))
            .ToHashSet(StringComparer.Ordinal);

        string[] missing = usedKeys.Where(key => !definedKeys.Contains(key)).Order().ToArray();

        Assert.IsEmpty(missing, $"Missing DynamicResource keys: {string.Join(", ", missing)}");
    }

    [TestMethod]
    public void FeatureXaml_DoesNotHardcodePaletteTypographyOrCornerRadius()
    {
        string projectRoot = Path.Combine(GetSolutionRoot(), "DeepDroidChanger");
        string[] viewFiles = Directory.GetFiles(Path.Combine(projectRoot, "Views"), "*.xaml", SearchOption.AllDirectories);
        string[] files = viewFiles.Append(Path.Combine(projectRoot, "MainWindow.xaml"))
            .ToArray();
        var violations = new List<string>();

        foreach (string path in files)
        {
            string text = File.ReadAllText(path);
            if (HexColorRegex().IsMatch(text)
                || NumericFontSizeRegex().IsMatch(text)
                || NumericCornerRadiusRegex().IsMatch(text)
                || UnsupportedFontWeightRegex().IsMatch(text))
            {
                violations.Add(Path.GetRelativePath(projectRoot, path));
            }
        }

        Assert.IsEmpty(violations, $"Hardcoded visual tokens found in: {string.Join(", ", violations)}");
    }

    [TestMethod]
    public void ThemeFolder_ContainsOnlyCanonicalThemeAndControlDictionaries()
    {
        string themesRoot = Path.Combine(GetSolutionRoot(), "DeepDroidChanger", "Resources", "Themes");
        HashSet<string> allowedNames = new(StringComparer.Ordinal)
        {
            "Theme.Light.xaml",
            "Theme.Dark.xaml",
            "DesignTokens.xaml",
            "Controls.xaml",
            "Control.xaml",
            "WindowControl.xaml",
            "GridControl.xaml",
            "BorderControl.xaml",
            "TextBlockControl.xaml",
            "PackIconControl.xaml",
            "ButtonControl.xaml",
            "TextBoxControl.xaml",
            "PasswordBoxControl.xaml",
            "ComboBoxControl.xaml",
            "CheckBoxControl.xaml",
            "RadioButtonControl.xaml",
            "ListBoxControl.xaml",
            "ListViewControl.xaml",
            "DataGridControl.xaml",
            "TabControl.xaml",
            "MenuControl.xaml",
            "ProgressBarControl.xaml",
            "SeparatorControl.xaml",
            "ToolTipControl.xaml",
            "GroupBoxControl.xaml",
            "SliderControl.xaml",
            "DatePickerControl.xaml",
            "ScrollBarControl.xaml",
        };
        string[] files = Directory.GetFiles(themesRoot, "*.xaml", SearchOption.TopDirectoryOnly);
        string[] unexpected = files
            .Select(Path.GetFileName)
            .Where(name => name == null || !allowedNames.Contains(name))
            .Select(name => name ?? "<unknown>")
            .ToArray();
        string[] legacyKeys = files
            .SelectMany(path => KeyDefinitionRegex().Matches(File.ReadAllText(path))
                .Select(match => match.Groups[1].Value)
                .Where(key => key.Contains("Legacy", StringComparison.OrdinalIgnoreCase))
                .Select(key => $"{Path.GetFileName(path)}:{key}"))
            .ToArray();

        Assert.IsEmpty(unexpected, $"Unexpected or shared resource dictionaries found: {string.Join(", ", unexpected)}");
        Assert.IsEmpty(legacyKeys, $"Legacy theme keys found: {string.Join(", ", legacyKeys)}");
    }

    [TestMethod]
    public void ControlDictionaries_DeclareTheirStaticResourceDependencies()
    {
        string themesRoot = Path.Combine(GetSolutionRoot(), "DeepDroidChanger", "Resources", "Themes");
        string[] files = Directory.GetFiles(themesRoot, "*Control.xaml", SearchOption.TopDirectoryOnly);
        var violations = new List<string>();

        foreach (string path in files)
        {
            string text = File.ReadAllText(path);
            bool usesDesignToken = text.Contains("{StaticResource Metric.", StringComparison.Ordinal)
                || text.Contains("{StaticResource Spacing.", StringComparison.Ordinal)
                || text.Contains("{StaticResource Radius.", StringComparison.Ordinal);

            if (usesDesignToken
                && !text.Contains("Source=\"DesignTokens.xaml\"", StringComparison.Ordinal))
            {
                violations.Add($"{Path.GetFileName(path)} -> DesignTokens.xaml");
            }

            if (text.Contains("{StaticResource AppFocusVisualStyle}", StringComparison.Ordinal)
                && !text.Contains("x:Key=\"AppFocusVisualStyle\"", StringComparison.Ordinal)
                && !text.Contains("Source=\"Control.xaml\"", StringComparison.Ordinal))
            {
                violations.Add($"{Path.GetFileName(path)} -> Control.xaml");
            }
        }

        Assert.IsEmpty(
            violations,
            $"Control dictionaries have undeclared StaticResource dependencies: {string.Join(", ", violations)}");
    }

    [TestMethod]
    public void PaletteResourceReferences_UseCanonicalBrushPrefix()
    {
        string projectRoot = Path.Combine(GetSolutionRoot(), "DeepDroidChanger");
        string[] files = Directory.GetFiles(projectRoot, "*.xaml", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        string[] aliases = files
            .SelectMany(path => ResourceReferenceRegex().Matches(File.ReadAllText(path)).Select(match => match.Groups[1].Value.Trim()))
            .Where(key => key.Contains("Brush", StringComparison.Ordinal) && !key.StartsWith("Brush.", StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .Order()
            .ToArray();

        Assert.IsEmpty(aliases, $"Non-canonical brush resource aliases found: {string.Join(", ", aliases)}");
    }

    private static string GetSolutionRoot()
    {
        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
    }

    private static string[] GetPaletteKeys(string path)
    {
        return KeyDefinitionRegex().Matches(File.ReadAllText(path))
            .Select(match => match.Groups[1].Value)
            .Where(key => key.StartsWith("Color.", StringComparison.Ordinal)
                || key.StartsWith("Brush.", StringComparison.Ordinal))
            .Order()
            .ToArray();
    }

    [GeneratedRegex("x:Key=\"([^\"]+)\"")]
    private static partial Regex KeyDefinitionRegex();

    [GeneratedRegex("\\{DynamicResource\\s+([^},]+)")]
    private static partial Regex DynamicResourceRegex();

    [GeneratedRegex("\\{(?:Dynamic|Static)Resource\\s+([^},]+)")]
    private static partial Regex ResourceReferenceRegex();

    [GeneratedRegex("#[0-9A-Fa-f]{6,8}")]
    private static partial Regex HexColorRegex();

    [GeneratedRegex("(?:FontSize=\"[0-9]|Property=\"FontSize\"\\s+Value=\"[0-9])")]
    private static partial Regex NumericFontSizeRegex();

    [GeneratedRegex("(?:CornerRadius=\"[0-9]|Property=\"CornerRadius\"\\s+Value=\"[0-9])")]
    private static partial Regex NumericCornerRadiusRegex();

    [GeneratedRegex("FontWeight(?:=|\"\\s+Value=)\"(?:Normal|Regular|Medium)\"")]
    private static partial Regex UnsupportedFontWeightRegex();
}
