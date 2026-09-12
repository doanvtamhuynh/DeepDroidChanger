using System.Buffers.Binary;
using System.IO;

namespace ScrcpyNet;

public readonly record struct ScrcpyVideoPacketHeader(
    bool IsConfig,
    bool IsKeyFrame,
    long PresentationTimeUs,
    int PacketSize)
{
    public const int HeaderLength = 12;
    public const int MaximumPacketSize = 64 * 1024 * 1024;

    private const ulong ConfigFlag = 1UL << 63;
    private const ulong KeyFrameFlag = 1UL << 62;
    private const ulong PresentationTimeMask = (1UL << 62) - 1;

    public static ScrcpyVideoPacketHeader Parse(ReadOnlySpan<byte> header)
    {
        if (header.Length < HeaderLength)
            throw new InvalidDataException(
                $"A scrcpy video packet header must contain {HeaderLength} bytes.");

        ulong ptsFlags = BinaryPrimitives.ReadUInt64BigEndian(header);
        uint packetSize = BinaryPrimitives.ReadUInt32BigEndian(header[8..]);
        if (packetSize == 0 || packetSize > MaximumPacketSize)
        {
            throw new InvalidDataException(
                $"The scrcpy video packet size {packetSize} is invalid.");
        }

        return new ScrcpyVideoPacketHeader(
            (ptsFlags & ConfigFlag) != 0,
            (ptsFlags & KeyFrameFlag) != 0,
            checked((long)(ptsFlags & PresentationTimeMask)),
            checked((int)packetSize));
    }
}
