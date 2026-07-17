using System.Text;

namespace DeepDroidChanger.Services;

internal static class AdbCleanupCommandBuilder
{
    internal static readonly string[] RmRfPackagePathTemplates =
    [
        "/data/data/{package}",
        "/data/user/0/{package}",
        "/data/user_de/0/{package}",
        "/data/media/0/Android/data/{package}",
        "/sdcard/Android/data/{package}",
        "/data/misc/profiles/cur/0/{package}",
        "/data/misc/profiles/ref/{package}",
        "/data/misc/profiles/ref/0/{package}"
    ];

    internal static string CreatePreserveDirectoryCommand(
        string patterns,
        IReadOnlyCollection<string>? excludedPaths = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(patterns);
        string exclusionClause = excludedPaths is { Count: > 0 }
            ? $"case \"$target\" in {string.Join("|", excludedPaths)}) continue ;; esac; "
            : string.Empty;

        return $"for target in {patterns}; do "
            + exclusionClause
            + "if [ -d \"$target\" ]; then "
            + "find \"$target\" -mindepth 1 -not -type d -delete || exit $?; "
            + "elif [ -e \"$target\" ] || [ -L \"$target\" ]; then "
            + "rm -f \"$target\" || exit $?; "
            + "fi; done";
    }

    internal static string CreateRemoveFilesCommand(IEnumerable<string> patterns)
    {
        string[] values = patterns
            .Where(pattern => !string.IsNullOrWhiteSpace(pattern))
            .ToArray();
        return values.Length == 0
            ? string.Empty
            : $"rm -f {string.Join(' ', values)} || exit $?";
    }

    internal static string CreateProtectedSystemFilesCommand(string patterns)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(patterns);
        return $"for target in {patterns}; do "
            + "case \"$target\" in /data/system/package*) continue ;; esac; "
            + "if [ -e \"$target\" ] || [ -L \"$target\" ]; then "
            + "rm -f \"$target\" || exit $?; "
            + "fi; done";
    }

    internal static string CreatePackageCleanupCommand(
        IEnumerable<string> packageNames,
        bool useRmRf)
    {
        string[] packages = packageNames
            .Select(NormalizePackageName)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(packageName => packageName, StringComparer.Ordinal)
            .ToArray();
        if (packages.Length == 0)
            return string.Empty;

        var script = new StringBuilder();
        script.Append("for package in ")
            .AppendJoin(' ', packages)
            .AppendLine("; do")
            .AppendLine("  am force-stop \"$package\" || exit $?");
        if (useRmRf)
        {
            string paths = string.Join(
                ' ',
                RmRfPackagePathTemplates.Select(path =>
                    $"\"{path.Replace("{package}", "$package", StringComparison.Ordinal)}\""));
            script.Append("  rm -rf ")
                .Append(paths)
                .AppendLine(" || exit $?");
        }
        else
        {
            script.AppendLine("  pm clear \"$package\" >/dev/null || exit $?");
        }

        return script.Append("done").ToString();
    }

    internal static string NormalizePackageName(string packageName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageName);
        string normalized = packageName.Trim();
        if (!normalized.All(character => char.IsLetterOrDigit(character) || character is '.' or '_'))
            throw new ArgumentException("Package name contains unsupported characters.", nameof(packageName));

        return normalized;
    }
}
