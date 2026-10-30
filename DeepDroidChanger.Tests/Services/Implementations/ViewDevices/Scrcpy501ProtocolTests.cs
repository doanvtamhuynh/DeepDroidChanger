using System.Buffers.Binary;
using System.Text;
using ScrcpyNet;

namespace DeepDroidChanger.Tests.Services.Implementations.ViewDevices;

[TestClass]
public sealed class Scrcpy501ProtocolTests
{
    [TestMethod]
    public async Task Startup_Reads64ByteNameAndCodec_LeavingSessionHeaderUntouched()
    {
        byte[] data = new byte[80];
        Encoding.UTF8.GetBytes("Điện thoại 😀").CopyTo(data, 0);
        BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(64), 0x68323634);
        BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(68), 0x80000001);
        BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(72), 720);
        BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(76), 1600);
        using MemoryStream stream = new(data);
        Assert.AreEqual("Điện thoại 😀", await ScrcpyStartupIo.ReadVideoMetadataAsync(stream, CancellationToken.None));
        Assert.AreEqual(68, stream.Position);
        var session = ScrcpyVideoPacketHeader.Parse(data.AsSpan(68));
        Assert.IsTrue(session.IsSession);
        Assert.AreEqual(720, session.Width); Assert.AreEqual(1600, session.Height);
        Assert.AreEqual(0, session.PacketSize);
    }

    [TestMethod]
    public async Task Startup_RejectsTruncatedMetadataAndUnsupportedCodec()
    {
        using MemoryStream truncated = new(new byte[67]);
        await Assert.ThrowsExactlyAsync<EndOfStreamException>(() => ScrcpyStartupIo.ReadVideoMetadataAsync(truncated, CancellationToken.None));
        using MemoryStream unsupported = new(new byte[68]);
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() => ScrcpyStartupIo.ReadVideoMetadataAsync(unsupported, CancellationToken.None));
    }

    [TestMethod]
    public void StartupOptions_ExplicitAudioControlAndUniqueSessionAreSerialized()
    {
        string[] arguments = Scrcpy.BuildServerArguments(4_000_000, 1280, 30, true, false, 0x1234,
            "/data/local/tmp/deepdroid-scrcpy-00001234.jar").ToArray();
        foreach (string expected in new[] { "5.0.1", "audio=true", "control=false", "audio_codec=raw", "audio_source=output",
            "scid=00001234", "send_stream_meta=true", "send_frame_meta=true", "send_dummy_byte=false",
            "CLASSPATH=/data/local/tmp/deepdroid-scrcpy-00001234.jar" }) CollectionAssert.Contains(arguments, expected);
        CollectionAssert.DoesNotContain(arguments, "bit_rate=4000000");
    }

    [TestMethod]
    public async Task Receiver_ConsumesUhidOutputWithoutCorruptingFollowingClipboard()
    {
        string text = "Tiếng Việt e\u0301 😀 日本語\n\t";
        byte[] utf8 = Encoding.UTF8.GetBytes(text);
        byte[] message = new byte[6 + 5 + utf8.Length];
        new byte[] { 2, 0, 1, 0, 1, 3 }.CopyTo(message, 0);
        BinaryPrimitives.WriteInt32BigEndian(message.AsSpan(7), utf8.Length);
        utf8.CopyTo(message, 11);
        using MemoryStream stream = new(message);
        var hid = (ScrcpyUhidOutputDeviceMessage)await ScrcpyDeviceMessageReader.ReadAsync(stream);
        Assert.AreEqual(1, hid.Id); CollectionAssert.AreEqual(new byte[] { 3 }, hid.Data);
        var clipboard = (ScrcpyClipboardDeviceMessage)await ScrcpyDeviceMessageReader.ReadAsync(stream);
        Assert.AreEqual(text, clipboard.Text);
    }

    [TestMethod]
    public void HidMessages_UseOfficialCreateInputDestroyFrames()
    {
        byte[] create = new UhidCreateControlMessage().ToBytes().ToArray();
        CollectionAssert.AreEqual(new byte[] { 12, 0, 1, 0, 0, 0, 0, 0 }, create[..8]);
        Assert.AreEqual(ScrcpyHidKeyboard.ReportDescriptor.Length, BinaryPrimitives.ReadUInt16BigEndian(create.AsSpan(8)));
        CollectionAssert.AreEqual(ScrcpyHidKeyboard.ReportDescriptor, create[10..]);
        CollectionAssert.AreEqual(new byte[] { 13, 0, 1, 0, 8, 1, 0, 0x19, 0, 0, 0, 0, 0 },
            new UhidInputControlMessage { Data = [1, 0, 0x19, 0, 0, 0, 0, 0] }.ToBytes().ToArray());
        CollectionAssert.AreEqual(new byte[] { 14, 0, 1 }, new UhidDestroyControlMessage().ToBytes().ToArray());
        CollectionAssert.AreEqual(new byte[] { 15 }, new OpenHardKeyboardSettingsControlMessage().ToBytes().ToArray());
        CollectionAssert.AreEqual(new byte[] { 17 }, new ResetVideoControlMessage().ToBytes().ToArray());
    }

    [TestMethod]
    public void HidReports_PreserveModifiersAndRolloverAndReleaseEverything()
    {
        ScrcpyHidKeyboard hid = new();
        hid.SetKey(0xE0, true);
        CollectionAssert.AreEqual(new byte[] { 1, 0, 4, 0, 0, 0, 0, 0 }, hid.SetKey(4, true));
        for (byte key = 5; key <= 10; key++) hid.SetKey(key, true);
        CollectionAssert.AreEqual(new byte[] { 1, 0, 1, 1, 1, 1, 1, 1 }, hid.SetKey(10, true));
        CollectionAssert.AreEqual(new byte[8], hid.ReleaseAll());
        hid.UpdateLeds(new byte[] { 0 });
        Assert.HasCount(4, hid.SynchronizeLocks(3, 4));
        Assert.IsEmpty(hid.SynchronizeLocks(3, 4));
    }
}
