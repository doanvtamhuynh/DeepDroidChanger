using System;
using System.Buffers.Binary;
using System.Text;

namespace ScrcpyNet
{
    public enum ControlMessageType : byte
    {
        InjectKeycode,
        InjectText,
        InjectTouchEvent,
        InjectScrollEvent,
        BackOrScreenOn,
        ExpandNotificationPanel,
        ExpandSettingsPanel,
        CollapsePanels,
        GetClipboard,
        SetClipboard,
        SetScreenPowerMode,
        RotateDevice,
    }

    public enum ScrcpyCopyKey : byte
    {
        None = 0,
        Copy = 1,
        Cut = 2
    }

    public record ScreenSize
    {
        public ushort Width;
        public ushort Height;
    }

    public record Point
    {
        public int X;
        public int Y;
    }

    // Not sure whether to use struct, record, or class for this.
    public record Position
    {
        public ScreenSize ScreenSize = new();
        public Point Point = new();

        public Span<byte> ToBytes()
        {
            Span<byte> b = new byte[12];
            BinaryPrimitives.WriteInt32BigEndian(b[0..], Point.X);
            BinaryPrimitives.WriteInt32BigEndian(b[4..], Point.Y);
            BinaryPrimitives.WriteUInt16BigEndian(b[8..], ScreenSize.Width);
            BinaryPrimitives.WriteUInt16BigEndian(b[10..], ScreenSize.Height);
            return b;
        }
    }

    public interface IControlMessage
    {
        public ControlMessageType Type { get; }

        Span<byte> ToBytes();
    }

    internal static class ScrcpyTextEncoding
    {
        private static readonly Encoding Utf8 = new UTF8Encoding(
            encoderShouldEmitUTF8Identifier: false,
            throwOnInvalidBytes: true);

        public static byte[] EncodeTruncated(string text, int maxBytes)
        {
            ArgumentNullException.ThrowIfNull(text);
            ArgumentOutOfRangeException.ThrowIfNegative(maxBytes);

            byte[] encoded;
            try
            {
                encoded = Utf8.GetBytes(text);
            }
            catch (EncoderFallbackException exception)
            {
                throw new ArgumentException(
                    "The text contains an invalid UTF-16 sequence.",
                    nameof(text),
                    exception);
            }

            if (encoded.Length <= maxBytes)
                return encoded;

            int length = maxBytes;
            while (length > 0 &&
                   length < encoded.Length &&
                   (encoded[length] & 0xC0) == 0x80)
            {
                length--;
            }

            return encoded.AsSpan(0, length).ToArray();
        }
    }

    public class KeycodeControlMessage : IControlMessage
    {
        public ControlMessageType Type => ControlMessageType.InjectKeycode;
        public AndroidKeyEventAction Action { get; set; }
        public AndroidKeycode KeyCode { get; set; }
        public uint Repeat { get; set; }
        public AndroidMetastate Metastate { get; set; }

        public Span<byte> ToBytes()
        {
            Span<byte> b = new byte[14];
            b[0] = (byte)Type;
            b[1] = (byte)Action;
            BinaryPrimitives.WriteInt32BigEndian(b[2..], (int)KeyCode);
            BinaryPrimitives.WriteInt32BigEndian(b[6..], (int)Repeat);
            BinaryPrimitives.WriteInt32BigEndian(b[10..], (int)Metastate);
            return b;
        }
    }

    public sealed class InjectTextControlMessage : IControlMessage
    {
        public const int MaxTextBytes = 300;

        public ControlMessageType Type => ControlMessageType.InjectText;
        public string Text { get; set; } = string.Empty;

        public Span<byte> ToBytes()
        {
            byte[] utf8 = ScrcpyTextEncoding.EncodeTruncated(Text, MaxTextBytes);
            Span<byte> b = new byte[1 + sizeof(int) + utf8.Length];
            b[0] = (byte)Type;
            BinaryPrimitives.WriteInt32BigEndian(b[1..], utf8.Length);
            utf8.CopyTo(b[5..]);
            return b;
        }
    }

    public class BackOrScreenOnControlMessage : IControlMessage
    {
        public ControlMessageType Type => ControlMessageType.BackOrScreenOn;
        public AndroidKeyEventAction Action { get; set; }

        public Span<byte> ToBytes()
        {
            Span<byte> b = new byte[2];
            b[0] = (byte)Type;
            b[1] = (byte)Action;
            return b;
        }
    }

    public class TouchEventControlMessage : IControlMessage
    {
        public ControlMessageType Type => ControlMessageType.InjectTouchEvent;
        public AndroidMotionEventAction Action { get; set; }
        public AndroidMotionEventButtons Buttons { get; set; } = AndroidMotionEventButtons.AMOTION_EVENT_BUTTON_PRIMARY;
        public ulong PointerId { get; set; } = 0xFFFFFFFFFFFFFFFF;
        public Position Position { get; set; } = new();
        public float Pressure { get; set; } = 1f;

        public Span<byte> ToBytes()
        {
            Span<byte> b = new byte[28];
            b[0] = (byte)Type;
            b[1] = (byte)Action;
            BinaryPrimitives.WriteUInt64BigEndian(b[2..], PointerId);

            // Position
            BinaryPrimitives.WriteInt32BigEndian(b[10..], Position.Point.X);
            BinaryPrimitives.WriteInt32BigEndian(b[14..], Position.Point.Y);
            BinaryPrimitives.WriteUInt16BigEndian(b[18..], Position.ScreenSize.Width);
            BinaryPrimitives.WriteUInt16BigEndian(b[20..], Position.ScreenSize.Height);

            BinaryPrimitives.WriteUInt16BigEndian(b[22..], ToFixedPoint16(Pressure));

            BinaryPrimitives.WriteInt32BigEndian(b[24..], (int)Buttons);

            return b;
        }

        internal static ushort ToFixedPoint16(float pressure)
        {
            if (!float.IsFinite(pressure) || pressure < 0f || pressure > 1f)
                throw new ArgumentOutOfRangeException(nameof(pressure), "Pressure must be between 0 and 1.");

            // scrcpy v1.23 converts pressure by multiplying by 65536, truncating,
            // and clamping the result to the unsigned 16-bit wire range.
            uint fixedPoint = (uint)(pressure * 65536f);
            return (ushort)Math.Min((uint)ushort.MaxValue, fixedPoint);
        }
    }

    public class ScrollEventControlMessage : IControlMessage
    {
        public ControlMessageType Type => ControlMessageType.InjectScrollEvent;
        public Position Position { get; set; } = new();
        public int HorizontalScroll { get; set; }
        public int VerticalScroll { get; set; }
        public AndroidMotionEventButtons Buttons { get; set; }

        public Span<byte> ToBytes()
        {
            Span<byte> b = new byte[25];
            b[0] = (byte)Type;
            Position.ToBytes().CopyTo(b[1..]);
            BinaryPrimitives.WriteInt32BigEndian(b[13..], HorizontalScroll);
            BinaryPrimitives.WriteInt32BigEndian(b[17..], VerticalScroll);
            BinaryPrimitives.WriteInt32BigEndian(b[21..], (int)Buttons);
            return b;
        }
    }

    public sealed class GetClipboardControlMessage : IControlMessage
    {
        public ControlMessageType Type => ControlMessageType.GetClipboard;
        public ScrcpyCopyKey CopyKey { get; set; }

        public Span<byte> ToBytes()
        {
            if (!Enum.IsDefined(CopyKey))
                throw new ArgumentOutOfRangeException(nameof(CopyKey));

            Span<byte> b = new byte[2];
            b[0] = (byte)Type;
            b[1] = (byte)CopyKey;
            return b;
        }
    }

    public sealed class SetClipboardControlMessage : IControlMessage
    {
        public const int MaxTextBytes = (1 << 18) - 14;

        public ControlMessageType Type => ControlMessageType.SetClipboard;
        public ulong Sequence { get; set; }
        public bool Paste { get; set; }
        public string Text { get; set; } = string.Empty;

        public Span<byte> ToBytes()
        {
            byte[] utf8 = ScrcpyTextEncoding.EncodeTruncated(Text, MaxTextBytes);
            Span<byte> b = new byte[14 + utf8.Length];
            b[0] = (byte)Type;
            BinaryPrimitives.WriteUInt64BigEndian(b[1..], Sequence);
            b[9] = Paste ? (byte)1 : (byte)0;
            BinaryPrimitives.WriteInt32BigEndian(b[10..], utf8.Length);
            utf8.CopyTo(b[14..]);
            return b;
        }
    }

    public abstract class EmptyControlMessage : IControlMessage
    {
        public abstract ControlMessageType Type { get; }

        public Span<byte> ToBytes()
        {
            return new byte[] { (byte)Type };
        }
    }

    public sealed class ExpandNotificationPanelControlMessage : EmptyControlMessage
    {
        public override ControlMessageType Type => ControlMessageType.ExpandNotificationPanel;
    }

    public sealed class ExpandSettingsPanelControlMessage : EmptyControlMessage
    {
        public override ControlMessageType Type => ControlMessageType.ExpandSettingsPanel;
    }

    public sealed class CollapsePanelsControlMessage : EmptyControlMessage
    {
        public override ControlMessageType Type => ControlMessageType.CollapsePanels;
    }

    public class RotateDeviceControlMessage : IControlMessage
    {
        public ControlMessageType Type => ControlMessageType.RotateDevice;

        public Span<byte> ToBytes()
        {
            Span<byte> b = new byte[1];
            b[0] = (byte)Type;
            return b;
        }
    }

    public class SetScreenPowerModeControlMessage : IControlMessage
    {
        public ControlMessageType Type => ControlMessageType.SetScreenPowerMode;
        public AndroidScreenPowerMode Mode { get; set; } = AndroidScreenPowerMode.POWER_MODE_NORMAL;

        public Span<byte> ToBytes()
        {
            Span<byte> b = new byte[2];
            b[0] = (byte)Type;
            b[1] = (byte)Mode;
            return b;
        }
    }
}
