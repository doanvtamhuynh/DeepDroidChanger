namespace ScrcpyNet;

/// <summary>USB boot keyboard reports, matching scrcpy's hid_keyboard descriptor.</summary>
public sealed class ScrcpyHidKeyboard
{
    public const ushort DeviceId = 1;
    public static byte[] ReportDescriptor =>
    [
        0x05,0x01,0x09,0x06,0xA1,0x01,0x05,0x07,0x19,0xE0,0x29,0xE7,
        0x15,0x00,0x25,0x01,0x75,0x01,0x95,0x08,0x81,0x02,0x75,0x08,
        0x95,0x01,0x81,0x01,0x05,0x08,0x19,0x01,0x29,0x05,0x75,0x01,
        0x95,0x05,0x91,0x02,0x75,0x03,0x95,0x01,0x91,0x01,0x05,0x07,
        0x19,0x00,0x29,0x65,0x15,0x00,0x25,0x65,0x75,0x08,0x95,0x06,
        0x81,0x00,0xC0
    ];

    private readonly object gate = new();
    private readonly SortedSet<byte> pressed = [];
    private byte modifiers;
    private byte leds;

    public void UpdateLeds(ReadOnlySpan<byte> data)
    {
        if (data.Length == 1) lock (gate) leds = data[0];
    }

    public byte[] SetKey(byte usage, bool down)
    {
        lock (gate)
        {
            if (usage is >= 0xE0 and <= 0xE7)
            {
                byte bit = (byte)(1 << (usage - 0xE0));
                modifiers = down ? (byte)(modifiers | bit) : (byte)(modifiers & ~bit);
            }
            else if (down)
            {
                if (pressed.Add(usage))
                {
                    if (usage == 0x39) leds ^= 2;
                    else if (usage == 0x53) leds ^= 1;
                }
            }
            else pressed.Remove(usage);
            return CreateReport();
        }
    }

    public IReadOnlyList<IControlMessage> SynchronizeLocks(byte hostLeds, byte triggeringUsage)
    {
        lock (gate)
        {
            List<IControlMessage> messages = [];
            foreach ((byte flag, byte usage) in new (byte, byte)[] { (1, 0x53), (2, 0x39) })
            {
                if (triggeringUsage == usage || ((hostLeds ^ leds) & flag) == 0) continue;
                byte[] down = CreateReport();
                // Lock synchronization only when no normal key is held.
                if (pressed.Count != 0) continue;
                down[2] = usage;
                messages.Add(new UhidInputControlMessage { Data = down });
                messages.Add(new UhidInputControlMessage { Data = CreateReport() });
                leds ^= flag;
            }
            return messages;
        }
    }

    public byte[] ReleaseAll()
    {
        lock (gate) { pressed.Clear(); modifiers = 0; return CreateReport(); }
    }

    private byte[] CreateReport()
    {
        byte[] data = new byte[8];
        data[0] = modifiers;
        if (pressed.Count > 6) Array.Fill(data, (byte)1, 2, 6);
        else pressed.CopyTo(data, 2);
        return data;
    }
}
