using System.IO;
using DeepDroidChanger.Models;

namespace DeepDroidChanger.Services;

/// <summary>Fresh, owned operation directories; no persisted workflow state.</summary>
public sealed class TwrpOperationCache
{
    private static readonly object CreationGate = new();
    private readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, string> _owned = new();
    public string CacheRoot { get; }
    public TwrpOperationCache() : this(Path.Combine(AppContext.BaseDirectory, "cache")) { }
    internal TwrpOperationCache(string cacheRoot) => CacheRoot = Path.GetFullPath(cacheRoot);

    internal void Create(TwrpOperation operation)
    {
        lock (CreationGate) CreateOwned(operation);
    }

    private void CreateOwned(TwrpOperation operation)
    {
        string name = operation.Kind == TwrpOperationKind.FactoryReset
            ? "factory_reset_" + operation.Id.ToString("N")
            : TwrpSafety.CliBackupName(operation.ArchivePath!);
        TwrpSafety.ValidateLocalDirectory(CacheRoot);
        Directory.CreateDirectory(CacheRoot);
        string directory = Path.Combine(CacheRoot, name);
        // Preserve prior logs and never adopt or delete a prior operation's directory.
        if (Directory.Exists(directory) || File.Exists(directory))
            directory = Path.Combine(CacheRoot, name + "_attempt_" + operation.Id.ToString("N"));
        if (Directory.Exists(directory) || File.Exists(directory)) throw new TwrpWorkflowException("Twrp_UnsafePath");
        TwrpSafety.ValidateLocalDirectory(directory);
        Directory.CreateDirectory(directory);
        operation.CacheDirectory = directory;
        if (!_owned.TryAdd(operation.Id, directory)) throw new TwrpWorkflowException("Twrp_UnsafePath");
        Directory.CreateDirectory(Path.Combine(directory, "logs"));
        foreach (string log in new[] { "process.log", "recovery.log", "operation.log" })
            File.WriteAllText(Path.Combine(directory, "logs", log), "");
    }

    internal void Check(TwrpOperation operation)
    {
        string directory = Path.GetFullPath(operation.CacheDirectory!);
        if (!_owned.TryGetValue(operation.Id, out string? owned) || !string.Equals(directory, owned, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(Path.GetDirectoryName(directory), CacheRoot, StringComparison.OrdinalIgnoreCase))
            throw new TwrpWorkflowException("Twrp_UnsafePath");
        TwrpSafety.ValidateLocalDirectory(directory);
    }

    internal void Clean(TwrpOperation operation, bool success)
    {
        Check(operation);
        foreach (string child in Directory.EnumerateFileSystemEntries(operation.CacheDirectory!))
        {
            if (!success && Path.GetFileName(child) == "logs") continue;
            DeleteChecked(child);
        }
        if (success) Directory.Delete(operation.CacheDirectory!);
        _owned.TryRemove(operation.Id, out _);
    }

    internal static void DeleteChecked(string path)
    {
        TwrpSafety.ValidateLocalDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        if (File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint)) throw new TwrpWorkflowException("Twrp_UnsafePath");
        if (Directory.Exists(path))
        {
            foreach (string child in Directory.EnumerateFileSystemEntries(path)) DeleteChecked(child);
            Directory.Delete(path);
        }
        else File.Delete(path);
    }
}
