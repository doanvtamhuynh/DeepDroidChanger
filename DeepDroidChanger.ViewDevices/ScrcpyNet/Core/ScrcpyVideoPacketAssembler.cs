using System.IO;

namespace ScrcpyNet;

internal sealed class ScrcpyVideoPacketAssembler
{
    public const int MaximumPendingConfigSize = 4 * 1024 * 1024;

    private readonly object gate = new();
    private byte[]? pendingConfig;

    public void StoreConfig(ReadOnlySpan<byte> config)
    {
        if (config.Length == 0)
            throw new InvalidDataException("A scrcpy config packet cannot be empty.");

        lock (gate)
        {
            int existingLength = pendingConfig?.Length ?? 0;
            int combinedLength = checked(existingLength + config.Length);
            if (combinedLength > MaximumPendingConfigSize)
            {
                throw new InvalidDataException(
                    $"The pending scrcpy config exceeds {MaximumPendingConfigSize} bytes.");
            }

            byte[] combined = new byte[combinedLength];
            pendingConfig?.AsSpan().CopyTo(combined);
            config.CopyTo(combined.AsSpan(existingLength));
            pendingConfig = combined;
        }
    }

    public byte[]? ConsumeWith(ReadOnlySpan<byte> packet)
    {
        lock (gate)
        {
            if (pendingConfig is null)
                return null;

            int combinedLength = checked(pendingConfig.Length + packet.Length);
            if (combinedLength > ScrcpyVideoPacketHeader.MaximumPacketSize)
            {
                throw new InvalidDataException(
                    $"The scrcpy config and video packet exceed {ScrcpyVideoPacketHeader.MaximumPacketSize} bytes.");
            }

            byte[] combined = new byte[combinedLength];
            pendingConfig.AsSpan().CopyTo(combined);
            packet.CopyTo(combined.AsSpan(pendingConfig.Length));
            pendingConfig = null;
            return combined;
        }
    }

    public void Clear()
    {
        lock (gate)
            pendingConfig = null;
    }
}
