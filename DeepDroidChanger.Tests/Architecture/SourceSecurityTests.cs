using System.Text.RegularExpressions;

namespace DeepDroidChanger.Tests.Architecture;

[TestClass]
public sealed class SourceSecurityTests
{
    [TestMethod]
    public void SourceFiles_DoNotContainCredentialBearingUrlsOrRawProcessArgumentLogs()
    {
        string sourceRoot = Path.Combine(GetSolutionRoot(), "DeepDroidChanger");
        string[] sourceFiles = Directory.GetFiles(sourceRoot, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var credentialUrlPattern = new Regex(
            "https?://[^\\s\\\"]+[?&](?:key|api[_-]?key|token|secret)=[^&\\s\\\"]+",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        var sensitiveLogTemplatePattern = new Regex(
            "\\{(?:Path|QuarantinePath|Host|Url|PublicIp|InterfaceIp|Latitude|Longitude|Lat|Lon|Username|Password|Token|Input|Line)\\}",
            RegexOptions.CultureInvariant);

        foreach (string sourceFile in sourceFiles)
        {
            string source = File.ReadAllText(sourceFile);
            Assert.IsFalse(
                credentialUrlPattern.IsMatch(source),
                $"Credential-bearing URL found in '{sourceFile}'.");
            Assert.IsFalse(
                source.Contains("{Arguments}", StringComparison.Ordinal),
                $"Raw process arguments are logged in '{sourceFile}'.");
            Assert.IsFalse(
                source.Contains("Output: {Output}", StringComparison.Ordinal) ||
                source.Contains("Error: {Error}", StringComparison.Ordinal),
                $"Raw process output is logged in '{sourceFile}'.");
            Assert.IsFalse(
                source.Contains("http://", StringComparison.OrdinalIgnoreCase),
                $"Plaintext HTTP endpoint found in '{sourceFile}'.");
            Assert.IsFalse(
                sensitiveLogTemplatePattern.IsMatch(source),
                $"Sensitive value is included in a log template in '{sourceFile}'.");
        }
    }

    private static string GetSolutionRoot()
    {
        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
    }
}
