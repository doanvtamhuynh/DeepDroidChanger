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

    public bool IsSession { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }
    private const ulong SessionFlag = 1UL << 63;
    private const ulong ConfigFlag = 1UL << 62;
    private const ulong KeyFrameFlag = 1UL << 61;
    private const ulong PresentationTimeMask = (1UL << 61) - 1;

    public static ScrcpyVideoPacketHeader Parse(ReadOnlySpan<byte> header)
    {
        if (header.Length < HeaderLength)
            throw new InvalidDataException(
                $"A scrcpy video packet header must contain {HeaderLength} bytes.");

        ulong ptsFlags = BinaryPrimitives.ReadUInt64BigEndian(header);
        if ((ptsFlags & SessionFlag) != 0)
        {
            int width = BinaryPrimitives.ReadInt32BigEndian(header[4..]);
            int height = BinaryPrimitives.ReadInt32BigEndian(header[8..]);
            if (width <= 0 || height <= 0 || width > ushort.MaxValue || height > ushort.MaxValue)
                throw new InvalidDataException("Invalid scrcpy video session dimensions.");
            return new(false, false, 0, 0) { IsSession = true, Width = width, Height = height };
        }
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
