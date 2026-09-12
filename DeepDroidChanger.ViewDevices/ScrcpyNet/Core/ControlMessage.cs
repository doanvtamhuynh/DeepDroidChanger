using System;
using System.Buffers.Binary;

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

            return checked((ushort)MathF.Round(pressure * ushort.MaxValue));
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
