namespace ScrcpyNet;

public sealed class DecodedFrameSnapshot
{
    public int Width { get; }
    public int Height { get; }
    public ReadOnlyMemory<byte> Bgra32 { get; }

    internal DecodedFrameSnapshot(int width, int height, byte[] bgra32)
    {
        ArgumentNullException.ThrowIfNull(bgra32);

        if (width <= 0)
            throw new ArgumentOutOfRangeException(nameof(width));
        if (height <= 0)
            throw new ArgumentOutOfRangeException(nameof(height));

        int expectedLength = checked(width * height * 4);
        if (bgra32.Length != expectedLength)
        {
            throw new ArgumentException(
                $"A BGRA32 snapshot must contain exactly {expectedLength} bytes.",
                nameof(bgra32));
        }

        Width = width;
        Height = height;
        Bgra32 = bgra32;
    }
}
