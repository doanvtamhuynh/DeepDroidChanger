using System.Buffers.Binary;
using System.IO;
using System.Text;

namespace ScrcpyNet;

public enum ScrcpyDeviceMessageType : byte
{
    Clipboard = 0,
    AckClipboard = 1,
    UhidOutput = 2
}

public abstract record ScrcpyDeviceMessage(ScrcpyDeviceMessageType Type);

public sealed record ScrcpyClipboardDeviceMessage(string Text)
    : ScrcpyDeviceMessage(ScrcpyDeviceMessageType.Clipboard)
{
    public string Text { get; } = Text ?? throw new ArgumentNullException(nameof(Text));
}

public sealed record ScrcpyClipboardAckDeviceMessage(ulong Sequence)
    : ScrcpyDeviceMessage(ScrcpyDeviceMessageType.AckClipboard);

public sealed record ScrcpyUhidOutputDeviceMessage(ushort Id, byte[] Data)
    : ScrcpyDeviceMessage(ScrcpyDeviceMessageType.UhidOutput);

public sealed class ScrcpyClipboardChangedEventArgs : EventArgs
{
    public ScrcpyClipboardChangedEventArgs(string text)
    {
        Text = text ?? throw new ArgumentNullException(nameof(text));
    }

    public string Text { get; }
}

public sealed class ScrcpyClipboardAcknowledgedEventArgs(ulong sequence) : EventArgs
{
    public ulong Sequence { get; } = sequence;
}

internal static class ScrcpyDeviceMessageReader
{
    public const int MaxMessageSize = 1 << 18;
    public const int MaxClipboardTextLength = MaxMessageSize - 5;

    private static readonly Encoding Utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    public static async Task<ScrcpyDeviceMessage> ReadAsync(
        Stream stream,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);

        byte[] typeBuffer = new byte[1];
        await ReadExactAsync(stream, typeBuffer, cancellationToken).ConfigureAwait(false);

        switch ((ScrcpyDeviceMessageType)typeBuffer[0])
        {
            case ScrcpyDeviceMessageType.Clipboard:
            {
                byte[] lengthBuffer = new byte[sizeof(uint)];
                await ReadExactAsync(stream, lengthBuffer, cancellationToken).ConfigureAwait(false);
                uint length = BinaryPrimitives.ReadUInt32BigEndian(lengthBuffer);
                if (length > MaxClipboardTextLength)
                {
                    throw new InvalidDataException(
                        $"The scrcpy clipboard message is {length} bytes, exceeding the {MaxClipboardTextLength}-byte limit.");
                }

                byte[] textBuffer = new byte[(int)length];
                await ReadExactAsync(stream, textBuffer, cancellationToken).ConfigureAwait(false);
                string text;
                try
                {
                    text = Utf8.GetString(textBuffer);
                }
                catch (DecoderFallbackException exception)
                {
                    throw new InvalidDataException(
                        "The scrcpy clipboard message is not valid UTF-8.",
                        exception);
                }

                return new ScrcpyClipboardDeviceMessage(text);
            }
            case ScrcpyDeviceMessageType.AckClipboard:
            {
                byte[] sequenceBuffer = new byte[sizeof(ulong)];
                await ReadExactAsync(stream, sequenceBuffer, cancellationToken).ConfigureAwait(false);
                return new ScrcpyClipboardAckDeviceMessage(
                    BinaryPrimitives.ReadUInt64BigEndian(sequenceBuffer));
            }
            case ScrcpyDeviceMessageType.UhidOutput:
            {
                byte[] header = new byte[4];
                await ReadExactAsync(stream, header, cancellationToken).ConfigureAwait(false);
                ushort id = BinaryPrimitives.ReadUInt16BigEndian(header);
                ushort length = BinaryPrimitives.ReadUInt16BigEndian(header.AsSpan(2));
                if (length > 4096) throw new InvalidDataException("UHID output exceeds 4096 bytes.");
                byte[] data = new byte[length];
                await ReadExactAsync(stream, data, cancellationToken).ConfigureAwait(false);
                return new ScrcpyUhidOutputDeviceMessage(id, data);
            }
            default:
                throw new InvalidDataException(
                    $"Unknown scrcpy device message type {typeBuffer[0]}.");
        }
    }

    internal static async Task ReadExactAsync(
        Stream stream,
        byte[] buffer,
        CancellationToken cancellationToken)
    {
        int offset = 0;
        while (offset < buffer.Length)
        {
            int bytesRead = await stream
                .ReadAsync(buffer, offset, buffer.Length - offset, cancellationToken)
                .ConfigureAwait(false);
            if (bytesRead == 0)
                throw new EndOfStreamException("The scrcpy control receiver reached EOF mid-message.");

            offset += bytesRead;
        }
    }
}
