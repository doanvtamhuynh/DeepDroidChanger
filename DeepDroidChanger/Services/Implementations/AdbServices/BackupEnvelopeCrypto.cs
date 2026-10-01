using System.IO;

namespace DeepDroidChanger.Services;

/// <summary>
/// Recognizes the pre-V3 encrypted backup envelope. V3 archives are direct
/// ZIP files and intentionally do not expose a password-based crypto path.
/// </summary>
internal static class BackupEnvelopeCrypto
{
    private static readonly byte[] LegacyMagic = "DDCBACKUP"u8.ToArray();

    public static bool IsLegacyEnvelope(string archivePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(archivePath);

        using var stream = new FileStream(
            archivePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: LegacyMagic.Length,
            useAsync: false);
        Span<byte> header = stackalloc byte[LegacyMagic.Length];
        int read = stream.Read(header);
        return read == LegacyMagic.Length && header.SequenceEqual(LegacyMagic);
    }
}
