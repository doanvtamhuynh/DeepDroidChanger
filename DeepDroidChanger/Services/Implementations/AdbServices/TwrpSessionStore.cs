using System.IO;
using System.Text.Json;
using DeepDroidChanger.Helpers;
using DeepDroidChanger.Models;

namespace DeepDroidChanger.Services;

public sealed class TwrpSessionStore
{
    internal static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };
    private readonly SemaphoreSlim _gate = new(1, 1);
    public string Root { get; }
    public string CacheRoot { get; }
    public TwrpSessionStore() : this(Path.Combine(AppContext.BaseDirectory, "Backup", "TWRP"),
        Path.Combine(AppContext.BaseDirectory, "Backup", "cache")) { }
    internal TwrpSessionStore(string root) : this(root, Path.Combine(root, "cache")) { }
    internal TwrpSessionStore(string root, string cacheRoot)
    {
        Root = Path.GetFullPath(root);
        CacheRoot = Path.GetFullPath(cacheRoot);
    }
    private string EntryPath(string serial)
    {
        TwrpSafety.ValidateSerial(serial);
        TwrpSafety.ValidateLocalDirectory(Root);
        string path = Path.Combine(Root, "entry-" + serial + ".json");
        if (File.Exists(path) && File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint))
            throw new TwrpWorkflowException("Twrp_InvalidJournal");
        return path;
    }
    internal Task SaveEntryAsync(TwrpEntryIdentity identity, CancellationToken token) =>
        AtomicFileWriter.WriteAllTextAsync(EntryPath(identity.Serial), JsonSerializer.Serialize(identity, JsonOptions), token);
    internal async Task<TwrpEntryIdentity?> ReadEntryAsync(string serial, CancellationToken token)
    {
        string path = EntryPath(serial);
        if (!File.Exists(path)) return null;
        var value = JsonSerializer.Deserialize<TwrpEntryIdentity>(await File.ReadAllTextAsync(path, token).ConfigureAwait(false), JsonOptions);
        if (value is null || value.Serial != serial || !TwrpRecoveryImage.Available.Any(i => i.Device == value.DeviceMarker)
            || !Guid.TryParse(value.RecoveryBootId, out _)) throw new TwrpWorkflowException("Twrp_InvalidJournal");
        return value;
    }
    private string? CacheOwner(string directory)
    {
        TwrpSafety.ValidateLocalDirectory(directory);
        string marker = Path.Combine(directory, ".operation");
        if (!File.Exists(marker)) return null;
        if (File.GetAttributes(marker).HasFlag(FileAttributes.ReparsePoint)) throw new TwrpWorkflowException("Twrp_UnsafePath");
        return File.ReadAllText(marker);
    }
    public string DirectoryFor(Guid id)
    {
        if (id == Guid.Empty) throw new ArgumentException("Empty operation ID.", nameof(id));
        string legacy = Path.Combine(Root, id.ToString("N"));
        TwrpSafety.ValidateLocalDirectory(legacy);
        string? found = Directory.Exists(legacy) ? legacy : null;
        TwrpSafety.ValidateLocalDirectory(CacheRoot);
        if (Directory.Exists(CacheRoot))
            foreach (string directory in Directory.EnumerateDirectories(CacheRoot))
                if (CacheOwner(directory) == id.ToString("N") && (found is null || File.Exists(Path.Combine(directory, "session.json"))))
                {
                    if (found is not null) throw new TwrpWorkflowException("Twrp_InvalidJournal");
                    found = directory;
                }
        return found ?? legacy;
    }
    public string FileFor(Guid id, string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name != Path.GetFileName(name) || name.Contains(':') || name.Contains('\\') || name is "." or "..")
            throw new ArgumentException("Invalid operation filename.", nameof(name));
        string path = Path.Combine(DirectoryFor(id), name);
        if (File.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new TwrpWorkflowException("Twrp_UnsafePath");
        return path;
    }
    public async Task SaveAsync(TwrpSession session, CancellationToken token)
    {
        await _gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            if (session.CacheLayoutVersion == 1 && (session.CacheDirectory is null
                || !string.Equals(Path.GetDirectoryName(Path.GetFullPath(session.CacheDirectory)), CacheRoot, StringComparison.OrdinalIgnoreCase)
                || CacheOwner(session.CacheDirectory) != session.Id.ToString("N")
                || !string.Equals(DirectoryFor(session.Id), session.CacheDirectory, StringComparison.OrdinalIgnoreCase)))
                throw new TwrpWorkflowException("Twrp_UnsafePath");
            session.UpdatedAt = DateTimeOffset.UtcNow;
            await AtomicFileWriter.WriteAllTextAsync(FileFor(session.Id, "session.json"), JsonSerializer.Serialize(session, JsonOptions), token).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }
    public async Task<TwrpSession> LoadAsync(Guid id, CancellationToken token)
    {
        var session = JsonSerializer.Deserialize<TwrpSession>(await File.ReadAllTextAsync(FileFor(id, "session.json"), token).ConfigureAwait(false), JsonOptions)
            ?? throw new TwrpWorkflowException("Twrp_InvalidJournal");
        if (session.Id != id || session.FormatVersion != 1 || session.Device is null
            || !Enum.IsDefined(session.ExecutionMode) || !Enum.IsDefined(session.State) || !Enum.IsDefined(session.Kind))
            throw new TwrpWorkflowException("Twrp_InvalidJournal");
        TwrpSafety.ValidateSerial(session.Device.Serial);
        TwrpSafety.Storage(session.StorageRoot);
        if (session.ExecutionMode == TwrpExecutionMode.LegacyFlashedBoot)
        {
            if (session.Device.Codename != "sargo") throw new TwrpWorkflowException("Twrp_InvalidJournal");
            TwrpSafety.Staging(session.StagingRoot);
            TwrpSafety.Boot(session.Device.Slot);
        }
        else if (!TwrpRecoveryImage.Available.Any(i => i.Device == session.Device.Codename)
            || !session.Partitions.SequenceEqual(new[] { "data" }) || session.StorageRoot != "/data/media/0"
            || session.RecoveryDevice != session.Device.Codename || session.CacheLayoutVersion != 1
            || session.RunnerStarted && !Guid.TryParse(session.RecoveryBootId, out _))
            throw new TwrpWorkflowException("Twrp_InvalidJournal");
        if (session.CacheLayoutVersion is < 0 or > 1 || session.CacheLayoutVersion == 1
            && !string.Equals(DirectoryFor(id), session.CacheDirectory, StringComparison.OrdinalIgnoreCase))
            throw new TwrpWorkflowException("Twrp_InvalidJournal");
        return session;
    }
    public async Task<IReadOnlyList<TwrpSession>> ListAsync(string serial, CancellationToken token)
    {
        var result = new List<TwrpSession>();
        var ids = new HashSet<Guid>();
        TwrpSafety.ValidateLocalDirectory(Root);
        TwrpSafety.ValidateLocalDirectory(CacheRoot);
        if (Directory.Exists(Root))
            foreach (string directory in Directory.EnumerateDirectories(Root))
                if (Guid.TryParseExact(Path.GetFileName(directory), "N", out Guid id)) ids.Add(id);
        if (Directory.Exists(CacheRoot))
            foreach (string directory in Directory.EnumerateDirectories(CacheRoot))
            {
                TwrpSafety.ValidateLocalDirectory(directory);
                if (!File.Exists(Path.Combine(directory, "session.json"))) continue;
                if (!Guid.TryParseExact(CacheOwner(directory), "N", out Guid id)) throw new TwrpWorkflowException("Twrp_InvalidJournal");
                ids.Add(id);
            }
        foreach (Guid id in ids)
        {
            token.ThrowIfCancellationRequested();
            try
            {
                var session = await LoadAsync(id, token).ConfigureAwait(false);
                if (serial.Length == 0 || session.Device.Serial == serial) result.Add(session);
            }
            catch (Exception exception) when (exception is IOException or JsonException or TwrpWorkflowException)
            {
                // An unreadable journal may describe a boot write. Never hide it and
                // accidentally authorize a new session or deletion of rescue files.
                throw new TwrpWorkflowException("Twrp_InvalidJournal");
            }
        }
        return result.OrderByDescending(item => item.CreatedAt).ToArray();
    }
    public void DeleteLocal(Guid id)
    {
        string directory = DirectoryFor(id);
        TwrpSafety.ValidateLocalDirectory(directory);
        if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
    }
}
