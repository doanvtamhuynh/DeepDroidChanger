using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using System.Text;
using DeepDroidChanger.Services;
using System.Windows;
using System.Windows.Input;
using DeepDroidChanger.ViewDevices.Models;
using DeepDroidChanger.ViewDevices.Runtime;
using ScrcpyNet;
using ScrcpyNet.Wpf;
using SharpAdbClient;
using WpfPoint = System.Windows.Point;

namespace DeepDroidChanger.Tests.Services.Implementations.ViewDevices;

[TestClass]
public sealed class ScrcpyProtocolTests
{
    [TestMethod]
    public void InjectKeycode_UsesScrcpyV123GoldenBytes()
    {
        KeycodeControlMessage message = new()
        {
            Action = AndroidKeyEventAction.AKEY_EVENT_ACTION_UP,
            KeyCode = (AndroidKeycode)24,
            Repeat = 2,
            Metastate = AndroidMetastate.AMETA_CTRL_ON
        };

        AssertBytes(
            new byte[] { 0, 1, 0, 0, 0, 24, 0, 0, 0, 2, 0, 0, 0x10, 0 },
            message.ToBytes());
    }

    [TestMethod]
    public void InjectText_UsesScrcpyV123Utf8WireFormat()
    {
        InjectTextControlMessage message = new() { Text = "é" };

        AssertBytes(
            new byte[] { 1, 0, 0, 0, 2, 0xC3, 0xA9 },
            message.ToBytes());
    }

    [TestMethod]
    public void InjectText_UsesAsciiLengthAndPayload()
    {
        AssertBytes(
            new byte[] { 1, 0, 0, 0, 1, 0x41 },
            new InjectTextControlMessage { Text = "A" }.ToBytes());
    }

    [TestMethod]
    public void InjectText_TruncatesAtUtf8Boundary()
    {
        string text = string.Concat(
            Enumerable.Repeat("é", InjectTextControlMessage.MaxTextBytes / 2)) + "x";
        byte[] actual = new InjectTextControlMessage { Text = text }.ToBytes().ToArray();

        Assert.AreEqual(5 + InjectTextControlMessage.MaxTextBytes, actual.Length);
        Assert.AreEqual(
            text[..(InjectTextControlMessage.MaxTextBytes / 2)],
            Encoding.UTF8.GetString(actual, 5, InjectTextControlMessage.MaxTextBytes));
    }

    [TestMethod]
    public void ClipboardControlMessages_UseScrcpyV123GoldenBytes()
    {
        AssertBytes(
            new byte[] { 8, 0 },
            new GetClipboardControlMessage { CopyKey = ScrcpyCopyKey.None }.ToBytes());
        AssertBytes(
            new byte[] { 8, 1 },
            new GetClipboardControlMessage { CopyKey = ScrcpyCopyKey.Copy }.ToBytes());
        AssertBytes(
            new byte[] { 8, 2 },
            new GetClipboardControlMessage { CopyKey = ScrcpyCopyKey.Cut }.ToBytes());
        AssertBytes(
            new byte[]
            {
                9, 1, 2, 3, 4, 5, 6, 7, 8, 1,
                0, 0, 0, 2, 0xC3, 0xA9
            },
            new SetClipboardControlMessage
            {
                Sequence = 0x0102030405060708,
                Paste = true,
                Text = "é"
            }.ToBytes());
    }

    [TestMethod]
    public void CtrlV_UsesInvalidSequenceAndNativePasteFlag()
    {
        SetClipboardControlMessage message = new()
        {
            Sequence = 0,
            Paste = true,
            Text = "test"
        };

        AssertBytes(
            new byte[]
            {
                9, 0, 0, 0, 0, 0, 0, 0, 0, 1,
                0, 0, 0, 4, 0x74, 0x65, 0x73, 0x74
            },
            message.ToBytes());
    }

    [TestMethod]
    public void NativePaste_EncodesRequiredUnicodeTextAsValidUtf8()
    {
        string[] texts = ["test", "hello world", "Tiếng Việt", "日本語"];

        foreach (string text in texts)
        {
            byte[] actual = new SetClipboardControlMessage
            {
                Sequence = 0,
                Paste = true,
                Text = text
            }.ToBytes().ToArray();
            int byteCount = BinaryPrimitives.ReadInt32BigEndian(actual.AsSpan(10, sizeof(int)));

            Assert.AreEqual(Encoding.UTF8.GetByteCount(text), byteCount);
            Assert.AreEqual(
                text,
                Encoding.UTF8.GetString(actual, 14, byteCount));
        }
    }

    [TestMethod]
    public void SetClipboard_AllowsEmptyTextWithZeroPayloadLength()
    {
        AssertBytes(
            new byte[] { 9, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 },
            new SetClipboardControlMessage { Sequence = 0, Text = string.Empty }.ToBytes());
    }

    [TestMethod]
    public void SetClipboard_TruncatesAtUtf8Boundary()
    {
        string text = string.Concat(
            Enumerable.Repeat("é", SetClipboardControlMessage.MaxTextBytes / 2)) + "x";
        byte[] actual = new SetClipboardControlMessage { Text = text }.ToBytes().ToArray();

        Assert.AreEqual(14 + SetClipboardControlMessage.MaxTextBytes, actual.Length);
        Assert.AreEqual(
            SetClipboardControlMessage.MaxTextBytes,
            BinaryPrimitives.ReadInt32BigEndian(actual.AsSpan(10, sizeof(int))));
        Assert.AreEqual(
            text[..(SetClipboardControlMessage.MaxTextBytes / 2)],
            Encoding.UTF8.GetString(
                actual,
                14,
                SetClipboardControlMessage.MaxTextBytes));
    }

    [TestMethod]
    public void ClipboardPanelMessages_UseOneByteScrcpyV123Types()
    {
        AssertBytes(new byte[] { 5 }, new ExpandNotificationPanelControlMessage().ToBytes());
        AssertBytes(new byte[] { 6 }, new ExpandSettingsPanelControlMessage().ToBytes());
        AssertBytes(new byte[] { 7 }, new CollapsePanelsControlMessage().ToBytes());
    }

    [TestMethod]
    public void GetClipboard_RejectsUnknownCopyKey()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => new GetClipboardControlMessage
            {
                CopyKey = (ScrcpyCopyKey)99
            }.ToBytes().ToArray());
    }

    [TestMethod]
    public async Task DeviceMessageReader_HandlesFragmentedClipboardAndAckBackToBack()
    {
        byte[] clipboard = BuildDeviceClipboardMessage("from-device");
        byte[] ack = new byte[1 + sizeof(ulong)];
        ack[0] = (byte)ScrcpyDeviceMessageType.AckClipboard;
        BinaryPrimitives.WriteUInt64BigEndian(ack.AsSpan(1), 0x0102030405060708);
        using FragmentedReadStream stream = new(clipboard.Concat(ack).ToArray(), maxChunk: 1);

        ScrcpyClipboardDeviceMessage clipboardMessage =
            (ScrcpyClipboardDeviceMessage)await ScrcpyDeviceMessageReader.ReadAsync(stream);
        ScrcpyClipboardAckDeviceMessage ackMessage =
            (ScrcpyClipboardAckDeviceMessage)await ScrcpyDeviceMessageReader.ReadAsync(stream);

        Assert.AreEqual("from-device", clipboardMessage.Text);
        Assert.AreEqual(0x0102030405060708UL, ackMessage.Sequence);
    }

    [TestMethod]
    public async Task DeviceMessageReader_RejectsUnknownInvalidAndOversizedMessages()
    {
        await Assert.ThrowsExactlyAsync<InvalidDataException>(
            () => ScrcpyDeviceMessageReader.ReadAsync(new MemoryStream(new byte[] { 99 })));

        await Assert.ThrowsExactlyAsync<InvalidDataException>(
            () => ScrcpyDeviceMessageReader.ReadAsync(
                new MemoryStream(new byte[] { 0, 0, 0, 0, 1, 0xFF })));

        byte[] oversized = new byte[1 + sizeof(uint)];
        oversized[0] = (byte)ScrcpyDeviceMessageType.Clipboard;
        BinaryPrimitives.WriteUInt32BigEndian(
            oversized.AsSpan(1),
            (uint)ScrcpyDeviceMessageReader.MaxClipboardTextLength + 1);
        await Assert.ThrowsExactlyAsync<InvalidDataException>(
            () => ScrcpyDeviceMessageReader.ReadAsync(new MemoryStream(oversized)));
    }

    [TestMethod]
    public void ControlCommandQueue_RecreatesChannelWithoutReplayingPreviousSession()
    {
        ScrcpyControlCommandQueue queue = new();
        _ = queue.StartSession();
        Assert.IsTrue(queue.TryWrite(new CollapsePanelsControlMessage()));
        queue.EndSession();

        ScrcpyControlQueueSession activeSession = queue.StartSession();
        Assert.IsFalse(activeSession.TryRead(out _));
        Assert.IsTrue(queue.TryWrite(new ExpandSettingsPanelControlMessage()));
        Assert.IsTrue(activeSession.TryRead(out _));
        queue.EndSession();
        Assert.IsFalse(queue.TryWrite(new ExpandNotificationPanelControlMessage()));
    }

    [TestMethod]
    public async Task FlushWithEmptyQueue_Completes()
    {
        ScrcpyControlCommandQueue queue = new();
        ScrcpyControlQueueSession session = queue.StartSession();

        Task flush = queue.FlushAsync();
        ScrcpyControlQueueItem.FlushBarrier barrier =
            (ScrcpyControlQueueItem.FlushBarrier)
            await ReadNextQueueItemAsync(session).WaitAsync(TimeSpan.FromSeconds(1));

        Assert.IsFalse(flush.IsCompleted);
        Assert.IsTrue(barrier.Completion.TrySetResult());
        await flush.WaitAsync(TimeSpan.FromSeconds(1));
        queue.EndSession();
    }

    [TestMethod]
    public async Task FlushBehindOneBatch_CompletesOnlyAfterBatchProcessed()
    {
        ScrcpyControlCommandQueue queue = new();
        ScrcpyControlQueueSession session = queue.StartSession();
        ScrcpyControlCommandBatch batch = new([new CollapsePanelsControlMessage()]);
        Assert.AreEqual(
            ScrcpyControlCommandQueueWriteResult.Accepted,
            queue.TryWriteBatchDetailed(batch));

        Task flush = queue.FlushAsync();
        ScrcpyControlQueueItem firstItem =
            await ReadNextQueueItemAsync(session).WaitAsync(TimeSpan.FromSeconds(1));
        Assert.AreSame(batch, ((ScrcpyControlQueueItem.Batch)firstItem).Value);
        Assert.IsFalse(flush.IsCompleted);

        ScrcpyControlQueueItem.FlushBarrier barrier = await ReadFlushBarrierAsync(session)
            .WaitAsync(TimeSpan.FromSeconds(1));
        Assert.IsFalse(flush.IsCompleted);
        barrier.Completion.TrySetResult();
        await flush.WaitAsync(TimeSpan.FromSeconds(1));
        queue.EndSession();
    }

    [TestMethod]
    public async Task FlushBehindMultipleBatches_PreservesFIFO()
    {
        ScrcpyControlCommandQueue queue = new();
        ScrcpyControlQueueSession session = queue.StartSession();
        ScrcpyControlCommandBatch first = new([new CollapsePanelsControlMessage()]);
        ScrcpyControlCommandBatch second = new([new ExpandSettingsPanelControlMessage()]);
        Assert.AreEqual(
            ScrcpyControlCommandQueueWriteResult.Accepted,
            queue.TryWriteBatchDetailed(first));
        Assert.AreEqual(
            ScrcpyControlCommandQueueWriteResult.Accepted,
            queue.TryWriteBatchDetailed(second));

        Task flush = queue.FlushAsync();
        ScrcpyControlQueueItem firstItem =
            await ReadNextQueueItemAsync(session).WaitAsync(TimeSpan.FromSeconds(1));
        ScrcpyControlQueueItem secondItem =
            await ReadNextQueueItemAsync(session).WaitAsync(TimeSpan.FromSeconds(1));
        Assert.AreSame(first, ((ScrcpyControlQueueItem.Batch)firstItem).Value);
        Assert.AreSame(second, ((ScrcpyControlQueueItem.Batch)secondItem).Value);

        ScrcpyControlQueueItem.FlushBarrier barrier = await ReadFlushBarrierAsync(session)
            .WaitAsync(TimeSpan.FromSeconds(1));
        Assert.IsFalse(flush.IsCompleted);
        barrier.Completion.TrySetResult();
        await flush.WaitAsync(TimeSpan.FromSeconds(1));
        queue.EndSession();
    }

    [TestMethod]
    public async Task FlushBarrier_DoesNotEmitWirePayload()
    {
        ScrcpyControlCommandQueue queue = new();
        ScrcpyControlQueueSession session = queue.StartSession();
        List<byte> sent = [];
        TaskCompletionSource batchProcessed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Task writer = Scrcpy.RunControlWriterAsync(
            session,
            static () => true,
            (batch, _) =>
            {
                sent.AddRange(batch.Payload.ToArray());
                batchProcessed.TrySetResult();
                return ValueTask.CompletedTask;
            },
            CancellationToken.None);
        ScrcpyControlCommandBatch batch = new([new CollapsePanelsControlMessage()]);

        Assert.AreEqual(
            ScrcpyControlCommandQueueWriteResult.Accepted,
            queue.TryWriteBatchDetailed(batch));
        Task flush = queue.FlushAsync();

        await batchProcessed.Task.WaitAsync(TimeSpan.FromSeconds(1));
        await flush.WaitAsync(TimeSpan.FromSeconds(1));
        queue.EndSession();
        await writer.WaitAsync(TimeSpan.FromSeconds(1));

        CollectionAssert.AreEqual(batch.Payload.ToArray(), sent);
    }

    [TestMethod]
    public async Task FlushWhenQueueInitiallyFull_WaitsForSpaceThenCompletes()
    {
        ScrcpyControlCommandQueue queue = new();
        ScrcpyControlQueueSession session = queue.StartSession();
        for (int i = 0; i < ScrcpyControlCommandQueue.Capacity; i++)
            Assert.IsTrue(queue.TryWrite(new CollapsePanelsControlMessage()));

        Task flush = queue.FlushAsync();
        Assert.IsFalse(flush.IsCompleted);

        ScrcpyControlQueueItem firstItem =
            await ReadNextQueueItemAsync(session).WaitAsync(TimeSpan.FromSeconds(1));
        Assert.IsInstanceOfType(firstItem, typeof(ScrcpyControlQueueItem.Batch));
        ScrcpyControlQueueItem.FlushBarrier barrier = await ReadFlushBarrierAsync(session)
            .WaitAsync(TimeSpan.FromSeconds(1));

        Assert.IsFalse(flush.IsCompleted);
        barrier.Completion.TrySetResult();
        await flush.WaitAsync(TimeSpan.FromSeconds(1));
        queue.EndSession();
    }

    [TestMethod]
    public async Task EndSession_CancelsPendingFlush()
    {
        ScrcpyControlCommandQueue queue = new();
        ScrcpyControlQueueSession session = queue.StartSession();
        Task flush = queue.FlushAsync();
        _ = await ReadFlushBarrierAsync(session).WaitAsync(TimeSpan.FromSeconds(1));

        queue.EndSession();

        await Assert.ThrowsAsync<OperationCanceledException>(() => flush);
    }

    [TestMethod]
    public async Task StartNewSession_CancelsOldSessionFlush()
    {
        ScrcpyControlCommandQueue queue = new();
        ScrcpyControlQueueSession oldSession = queue.StartSession();
        Task flush = queue.FlushAsync();
        _ = await ReadFlushBarrierAsync(oldSession).WaitAsync(TimeSpan.FromSeconds(1));

        ScrcpyControlQueueSession newSession = queue.StartSession();

        await Assert.ThrowsAsync<OperationCanceledException>(() => flush);
        Assert.IsFalse(newSession.TryRead(out _));
        queue.EndSession();
    }

    [TestMethod]
    public async Task FlushFromOldSessionNeverCompletesFromNewSessionWriter()
    {
        ScrcpyControlCommandQueue queue = new();
        ScrcpyControlQueueSession oldSession = queue.StartSession();
        Task oldFlush = queue.FlushAsync();
        _ = await ReadFlushBarrierAsync(oldSession).WaitAsync(TimeSpan.FromSeconds(1));

        ScrcpyControlQueueSession newSession = queue.StartSession();
        using CancellationTokenSource writerCancellation = new();
        TaskCompletionSource newBatchProcessed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Task writer = Scrcpy.RunControlWriterAsync(
            newSession,
            static () => true,
            (_, _) =>
            {
                newBatchProcessed.TrySetResult();
                return ValueTask.CompletedTask;
            },
            writerCancellation.Token);

        Assert.IsTrue(queue.TryWrite(new ExpandSettingsPanelControlMessage()));
        await newBatchProcessed.Task.WaitAsync(TimeSpan.FromSeconds(1));
        await Assert.ThrowsAsync<OperationCanceledException>(() => oldFlush);

        queue.EndSession();
        writerCancellation.Cancel();
        await writer.WaitAsync(TimeSpan.FromSeconds(1));
    }

    [TestMethod]
    public void StartSession_RejectsAdmissionToOldSession()
    {
        ScrcpyControlCommandQueue queue = new();
        ScrcpyControlQueueSession oldSession = queue.StartSession();
        ScrcpyControlQueueSession newSession = queue.StartSession();

        Assert.AreEqual(
            ScrcpyControlCommandQueueWriteResult.Accepted,
            queue.TryWriteDetailed(new CollapsePanelsControlMessage()));
        Assert.IsFalse(oldSession.TryRead(out _));
        Assert.IsTrue(newSession.TryRead(out ScrcpyControlQueueItem? item));
        Assert.IsInstanceOfType(item, typeof(ScrcpyControlQueueItem.Batch));
        queue.EndSession();
    }

    [TestMethod]
    public void BatchPayload_IsFrozenAtConstruction()
    {
        TouchEventControlMessage message = CreateTouchMessage(
            AndroidMotionEventAction.AMOTION_EVENT_ACTION_MOVE,
            ScrcpyDisplay.PrimaryPointerId,
            10,
            20);
        ScrcpyControlCommandBatch batch = new([message]);
        byte[] payload = batch.Payload.ToArray();

        message.Action = AndroidMotionEventAction.AMOTION_EVENT_ACTION_UP;

        CollectionAssert.AreEqual(payload, batch.Payload.ToArray());
        Assert.AreEqual(
            (byte)AndroidMotionEventAction.AMOTION_EVENT_ACTION_MOVE,
            batch.Payload.Span[1]);
        Assert.IsTrue(batch.IsDroppable);
    }

    [TestMethod]
    public void MutationOfPositionAfterBatchCreation_DoesNotChangePayload()
    {
        Position position = CreatePosition(10, 20, 1080, 2220);
        TouchEventControlMessage message = CreateTouchMessage(
            AndroidMotionEventAction.AMOTION_EVENT_ACTION_MOVE,
            ScrcpyDisplay.PrimaryPointerId,
            10,
            20);
        message.Position = position;
        ScrcpyControlCommandBatch batch = new([message]);
        byte[] payload = batch.Payload.ToArray();

        position.Point.X = 999;
        position.Point.Y = 888;
        position.ScreenSize.Width = 400;
        position.ScreenSize.Height = 800;

        CollectionAssert.AreEqual(payload, batch.Payload.ToArray());
    }

    [TestMethod]
    public void MutationOfClipboardMessageAfterBatchCreation_DoesNotChangePayload()
    {
        SetClipboardControlMessage message = new()
        {
            Sequence = 1,
            Paste = false,
            Text = "before"
        };
        ScrcpyControlCommandBatch batch = new([message]);
        byte[] payload = batch.Payload.ToArray();

        message.Sequence = 2;
        message.Paste = true;
        message.Text = "after";

        CollectionAssert.AreEqual(payload, batch.Payload.ToArray());
    }

    [TestMethod]
    public void BatchPayload_PreservesExactMessageOrder()
    {
        IControlMessage[] messages =
        [
            new CollapsePanelsControlMessage(),
            new ExpandSettingsPanelControlMessage(),
            new BackOrScreenOnControlMessage
            {
                Action = AndroidKeyEventAction.AKEY_EVENT_ACTION_UP
            }
        ];
        ScrcpyControlCommandBatch batch = new(messages);

        byte[] expected = messages
            .SelectMany(static message => message.ToBytes().ToArray())
            .ToArray();
        CollectionAssert.AreEqual(expected, batch.Payload.ToArray());
        CollectionAssert.AreEqual(
            new[]
            {
                ControlMessageType.CollapsePanels,
                ControlMessageType.ExpandSettingsPanel,
                ControlMessageType.BackOrScreenOn
            },
            batch.MessageTypes.ToArray());
    }

    [TestMethod]
    public void InvalidMessageSerializationFailsDuringAdmission_AndValidBatchStillAdmits()
    {
        ScrcpyControlCommandQueue queue = new();
        ScrcpyControlQueueSession session = queue.StartSession();
        InjectTextControlMessage invalid = new() { Text = "\uD800" };

        Assert.ThrowsExactly<ArgumentException>(
            () => queue.TryWriteDetailed(invalid));
        Assert.AreEqual(
            ScrcpyControlCommandQueueWriteResult.Accepted,
            queue.TryWriteDetailed(new CollapsePanelsControlMessage()));
        Assert.IsTrue(session.TryRead(out ScrcpyControlQueueItem? item));
        ScrcpyControlCommandBatch admitted = ((ScrcpyControlQueueItem.Batch)item!).Value;
        Assert.AreEqual(ControlMessageType.CollapsePanels, admitted.MessageTypes[0]);
        queue.EndSession();
    }

    [TestMethod]
    public void Batch_RejectsEmptyOrNullMessages()
    {
        Assert.ThrowsExactly<ArgumentException>(
            () => new ScrcpyControlCommandBatch([]));
        Assert.ThrowsExactly<ArgumentException>(
            () => new ScrcpyControlCommandBatch(
                new IControlMessage[] { new CollapsePanelsControlMessage(), null! }));
    }

    [TestMethod]
    public void Batch_OwnsMessageCollection()
    {
        IControlMessage[] source = [new CollapsePanelsControlMessage()];
        ScrcpyControlCommandBatch batch = new(source);
        source[0] = new ExpandSettingsPanelControlMessage();

        Assert.AreEqual(1, batch.MessageCount);
        Assert.AreEqual(ControlMessageType.CollapsePanels, batch.MessageTypes[0]);
        CollectionAssert.AreEqual(new byte[] { 7 }, batch.Payload.ToArray());
    }

    [TestMethod]
    public void SingleMessage_WritesOneBatch()
    {
        ScrcpyControlCommandQueue queue = new();
        ScrcpyControlQueueSession session = queue.StartSession();
        CollapsePanelsControlMessage message = new();

        Assert.AreEqual(
            ScrcpyControlCommandQueueWriteResult.Accepted,
            queue.TryWriteDetailed(message));
        Assert.IsTrue(session.TryRead(out ScrcpyControlQueueItem? item));
        ScrcpyControlCommandBatch batch = ((ScrcpyControlQueueItem.Batch)item!).Value;
        Assert.AreEqual(1, batch.MessageCount);
        Assert.AreEqual(ControlMessageType.CollapsePanels, batch.MessageTypes[0]);
        CollectionAssert.AreEqual(new byte[] { 7 }, batch.Payload.ToArray());
    }

    [TestMethod]
    public void PowerPair_IsOneBatch()
    {
        ScrcpyControlCommandQueue queue = new();
        ScrcpyControlQueueSession session = queue.StartSession();
        IControlMessage[] messages =
        [
            new KeycodeControlMessage
            {
                Action = AndroidKeyEventAction.AKEY_EVENT_ACTION_DOWN,
                KeyCode = AndroidKeycode.AKEYCODE_POWER
            },
            new KeycodeControlMessage
            {
                Action = AndroidKeyEventAction.AKEY_EVENT_ACTION_UP,
                KeyCode = AndroidKeycode.AKEYCODE_POWER
            }
        ];

        Assert.AreEqual(
            ScrcpyControlCommandQueueWriteResult.Accepted,
            queue.TryWriteBatchDetailed(messages));
        Assert.IsTrue(session.TryRead(out ScrcpyControlQueueItem? item));
        ScrcpyControlCommandBatch batch = ((ScrcpyControlQueueItem.Batch)item!).Value;
        Assert.AreEqual(2, batch.MessageCount);
        Assert.AreEqual(ControlMessageType.InjectKeycode, batch.MessageTypes[0]);
        Assert.AreEqual(ControlMessageType.InjectKeycode, batch.MessageTypes[1]);
        Assert.AreEqual(
            (int)AndroidKeycode.AKEYCODE_POWER,
            BinaryPrimitives.ReadInt32BigEndian(batch.Payload.Span[2..6]));
        Assert.AreEqual(
            (byte)AndroidKeyEventAction.AKEY_EVENT_ACTION_UP,
            batch.Payload.Span[15]);
    }

    [TestMethod]
    public void CtrlVPaste_IsOneNativePasteMessage()
    {
        ScrcpyControlCommandBatch batch = new(
        [
            new SetClipboardControlMessage
            {
                Sequence = 0,
                Paste = true,
                Text = "paste"
            }
        ]);

        Assert.AreEqual(1, batch.MessageCount);
        CollectionAssert.AreEqual(
            new[] { ControlMessageType.SetClipboard },
            batch.MessageTypes.ToArray());
        Assert.IsFalse(batch.IsDroppable);
    }

    [TestMethod]
    public void PinchMovePair_IsOneDroppableBatch()
    {
        ScrcpyControlCommandBatch batch = new(
        [
            CreateTouchMessage(
                AndroidMotionEventAction.AMOTION_EVENT_ACTION_MOVE,
                ScrcpyDisplay.PrimaryPointerId,
                100,
                200),
            CreateTouchMessage(
                AndroidMotionEventAction.AMOTION_EVENT_ACTION_MOVE,
                ScrcpyDisplay.VirtualPointerId,
                980,
                2020)
        ]);

        Assert.AreEqual(2, batch.MessageCount);
        CollectionAssert.AreEqual(
            new[] { ControlMessageType.InjectTouchEvent, ControlMessageType.InjectTouchEvent },
            batch.MessageTypes.ToArray());
        Assert.IsTrue(batch.IsDroppable);
    }

    [TestMethod]
    public void PinchUpPair_IsCritical()
    {
        ScrcpyControlCommandBatch batch = new(
        [
            CreateTouchMessage(
                AndroidMotionEventAction.AMOTION_EVENT_ACTION_UP,
                ScrcpyDisplay.PrimaryPointerId,
                100,
                200),
            CreateTouchMessage(
                AndroidMotionEventAction.AMOTION_EVENT_ACTION_UP,
                ScrcpyDisplay.VirtualPointerId,
                980,
                2020)
        ]);

        Assert.AreEqual(2, batch.MessageCount);
        CollectionAssert.AreEqual(
            new[] { ControlMessageType.InjectTouchEvent, ControlMessageType.InjectTouchEvent },
            batch.MessageTypes.ToArray());
        Assert.IsFalse(batch.IsDroppable);
    }

    [TestMethod]
    public void QueueFull_DroppableBatch_RejectsWholeBatch()
    {
        ScrcpyControlCommandQueue queue = new();
        ScrcpyControlQueueSession session = queue.StartSession();
        for (int i = 0; i < ScrcpyControlCommandQueue.DroppableSoftLimit; i++)
        {
            Assert.AreEqual(
                ScrcpyControlCommandQueueWriteResult.Accepted,
                queue.TryWriteBatchDetailed(
                [
                    CreateTouchMessage(
                        AndroidMotionEventAction.AMOTION_EVENT_ACTION_MOVE,
                        ScrcpyDisplay.PrimaryPointerId,
                        i,
                        i),
                    CreateTouchMessage(
                        AndroidMotionEventAction.AMOTION_EVENT_ACTION_MOVE,
                        ScrcpyDisplay.VirtualPointerId,
                        i + 1,
                        i + 1)
                ]));
        }

        IControlMessage[] rejected =
        [
            CreateTouchMessage(
                AndroidMotionEventAction.AMOTION_EVENT_ACTION_MOVE,
                ScrcpyDisplay.PrimaryPointerId,
                1000,
                1000),
            CreateTouchMessage(
                AndroidMotionEventAction.AMOTION_EVENT_ACTION_MOVE,
                ScrcpyDisplay.VirtualPointerId,
                1001,
                1001)
        ];
        Assert.AreEqual(
            ScrcpyControlCommandQueueWriteResult.Full,
            queue.TryWriteBatchDetailed(rejected));
        Assert.IsTrue(session.TryRead(out ScrcpyControlQueueItem? firstItem));
        ScrcpyControlCommandBatch first = ((ScrcpyControlQueueItem.Batch)firstItem!).Value;
        Assert.AreEqual(2, first.MessageCount);
        int count = 1;
        while (session.TryRead(out _))
            count++;
        Assert.AreEqual(ScrcpyControlCommandQueue.DroppableSoftLimit, count);
    }

    [TestMethod]
    public void QueueFull_CriticalBatch_RejectsWholeBatch()
    {
        ScrcpyControlCommandQueue queue = new();
        ScrcpyControlQueueSession session = queue.StartSession();
        for (int i = 0; i < ScrcpyControlCommandQueue.Capacity; i++)
            Assert.IsTrue(queue.TryWrite(new CollapsePanelsControlMessage()));

        IControlMessage[] rejected =
        [
            new KeycodeControlMessage
            {
                Action = AndroidKeyEventAction.AKEY_EVENT_ACTION_DOWN,
                KeyCode = AndroidKeycode.AKEYCODE_POWER
            },
            new KeycodeControlMessage
            {
                Action = AndroidKeyEventAction.AKEY_EVENT_ACTION_UP,
                KeyCode = AndroidKeycode.AKEYCODE_POWER
            }
        ];
        Assert.AreEqual(
            ScrcpyControlCommandQueueWriteResult.Full,
            queue.TryWriteBatchDetailed(rejected));
        int count = 0;
        while (session.TryRead(out ScrcpyControlQueueItem? _))
            count++;
        Assert.AreEqual(ScrcpyControlCommandQueue.Capacity, count);
    }

    [TestMethod]
    public void DroppableSoftLimit_Is56_WhenCapacity64Reserve8()
    {
        Assert.IsTrue(object.Equals(64, ScrcpyControlCommandQueue.Capacity));
        Assert.IsTrue(object.Equals(8, ScrcpyControlCommandQueue.CriticalReserve));
        Assert.IsTrue(object.Equals(56, ScrcpyControlCommandQueue.DroppableSoftLimit));
    }

    [TestMethod]
    public void FillDroppableSoftLimit_AnotherMoveIsRejected()
    {
        ScrcpyControlCommandQueue queue = new();
        ScrcpyControlQueueSession session = queue.StartSession();
        FillDroppableSoftLimit(queue);

        Assert.AreEqual(
            ScrcpyControlCommandQueueWriteResult.Full,
            queue.TryWriteDetailed(CreateMoveMessage(1000)));

        queue.EndSession();
    }

    [TestMethod]
    public void FillDroppableSoftLimit_CriticalKeyUpStillAdmits()
    {
        ScrcpyControlCommandQueue queue = new();
        _ = queue.StartSession();
        FillDroppableSoftLimit(queue);

        Assert.AreEqual(
            ScrcpyControlCommandQueueWriteResult.Accepted,
            queue.TryWriteDetailed(CreateKeyMessage(AndroidKeyEventAction.AKEY_EVENT_ACTION_UP)));
        queue.EndSession();
    }

    [TestMethod]
    public void FillDroppableSoftLimit_PointerUpRecoveryStillAdmits()
    {
        ScrcpyControlCommandQueue queue = new();
        _ = queue.StartSession();
        FillDroppableSoftLimit(queue);

        Assert.AreEqual(
            ScrcpyControlCommandQueueWriteResult.Accepted,
            queue.TryWriteDetailed(
                CreateTouchMessage(
                    AndroidMotionEventAction.AMOTION_EVENT_ACTION_UP,
                    ScrcpyDisplay.PrimaryPointerId,
                    10,
                    20)));
        queue.EndSession();
    }

    [TestMethod]
    public void FillDroppableSoftLimit_NativePasteBatchStillAdmits()
    {
        ScrcpyControlCommandQueue queue = new();
        _ = queue.StartSession();
        FillDroppableSoftLimit(queue);

        Assert.AreEqual(
            ScrcpyControlCommandQueueWriteResult.Accepted,
            queue.TryWriteBatchDetailed(
                new ScrcpyControlCommandBatch(
                [
                    new SetClipboardControlMessage
                    {
                        Sequence = 0,
                        Paste = true,
                        Text = "paste"
                    }
                ])));
        queue.EndSession();
    }

    [TestMethod]
    public void CriticalReserve_CanUseEightRemainingPhysicalSlots()
    {
        ScrcpyControlCommandQueue queue = new();
        ScrcpyControlQueueSession session = queue.StartSession();
        FillDroppableSoftLimit(queue);

        for (int i = 0; i < ScrcpyControlCommandQueue.CriticalReserve; i++)
        {
            Assert.AreEqual(
                ScrcpyControlCommandQueueWriteResult.Accepted,
                queue.TryWriteDetailed(new CollapsePanelsControlMessage()));
        }

        Assert.AreEqual(
            ScrcpyControlCommandQueueWriteResult.Full,
            queue.TryWriteDetailed(new CollapsePanelsControlMessage()));

        int count = 0;
        while (session.TryRead(out _))
            count++;
        Assert.AreEqual(ScrcpyControlCommandQueue.Capacity, count);
        queue.EndSession();
    }

    [TestMethod]
    public void HardCapacityFullOfCriticalWork_StillReturnsFull()
    {
        ScrcpyControlCommandQueue queue = new();
        _ = queue.StartSession();
        for (int i = 0; i < ScrcpyControlCommandQueue.Capacity; i++)
            Assert.IsTrue(queue.TryWrite(new CollapsePanelsControlMessage()));

        Assert.AreEqual(
            ScrcpyControlCommandQueueWriteResult.Full,
            queue.TryWriteDetailed(new KeycodeControlMessage
            {
                Action = AndroidKeyEventAction.AKEY_EVENT_ACTION_UP,
                KeyCode = AndroidKeycode.AKEYCODE_A
            }));
        queue.EndSession();
    }

    [TestMethod]
    public void DequeuedDroppable_ReleasesOneDroppablePermit()
    {
        ScrcpyControlCommandQueue queue = new();
        ScrcpyControlQueueSession session = queue.StartSession();
        FillDroppableSoftLimit(queue);

        Assert.AreEqual(
            ScrcpyControlCommandQueueWriteResult.Full,
            queue.TryWriteDetailed(CreateMoveMessage(1000)));
        Assert.IsTrue(session.TryRead(out _));
        Assert.AreEqual(
            ScrcpyControlCommandQueueWriteResult.Accepted,
            queue.TryWriteDetailed(CreateMoveMessage(1001)));
        queue.EndSession();
    }

    [TestMethod]
    public void RejectedDroppable_DoesNotLeakPermit()
    {
        ScrcpyControlCommandQueue queue = new();
        ScrcpyControlQueueSession session = queue.StartSession();
        FillDroppableSoftLimit(queue);

        Assert.AreEqual(
            ScrcpyControlCommandQueueWriteResult.Full,
            queue.TryWriteDetailed(CreateMoveMessage(1000)));
        while (session.TryRead(out _))
        {
        }

        for (int i = 0; i < ScrcpyControlCommandQueue.DroppableSoftLimit; i++)
        {
            Assert.AreEqual(
                ScrcpyControlCommandQueueWriteResult.Accepted,
                queue.TryWriteDetailed(CreateMoveMessage(i)));
        }
        queue.EndSession();
    }

    [TestMethod]
    public async Task FlushBehindSoftLimitTraffic_PreservesFIFO()
    {
        ScrcpyControlCommandQueue queue = new();
        ScrcpyControlQueueSession session = queue.StartSession();
        FillDroppableSoftLimit(queue);
        KeycodeControlMessage critical = CreateKeyMessage(AndroidKeyEventAction.AKEY_EVENT_ACTION_UP);
        Assert.AreEqual(
            ScrcpyControlCommandQueueWriteResult.Accepted,
            queue.TryWriteDetailed(critical));

        Task flush = queue.FlushAsync();
        for (int i = 0; i < ScrcpyControlCommandQueue.DroppableSoftLimit; i++)
        {
            ScrcpyControlQueueItem item = await ReadNextQueueItemAsync(session)
                .WaitAsync(TimeSpan.FromSeconds(1));
            ScrcpyControlCommandBatch batch = ((ScrcpyControlQueueItem.Batch)item).Value;
            Assert.IsTrue(batch.IsDroppable);
        }

        ScrcpyControlCommandBatch criticalBatch = ((ScrcpyControlQueueItem.Batch)
            await ReadNextQueueItemAsync(session).WaitAsync(TimeSpan.FromSeconds(1))).Value;
        Assert.IsFalse(criticalBatch.IsDroppable);
        Assert.AreEqual(ControlMessageType.InjectKeycode, criticalBatch.MessageTypes[0]);

        ScrcpyControlQueueItem.FlushBarrier barrier = await ReadFlushBarrierAsync(session)
            .WaitAsync(TimeSpan.FromSeconds(1));
        barrier.Completion.TrySetResult();
        await flush.WaitAsync(TimeSpan.FromSeconds(1));
        queue.EndSession();
    }

    [TestMethod]
    public async Task EndSession_WithSaturatedDroppableQueue_DoesNotHangFlush()
    {
        ScrcpyControlCommandQueue queue = new();
        _ = queue.StartSession();
        FillDroppableSoftLimit(queue);
        for (int i = 0; i < ScrcpyControlCommandQueue.CriticalReserve; i++)
            Assert.IsTrue(queue.TryWrite(new CollapsePanelsControlMessage()));

        Task flush = queue.FlushAsync();
        Assert.IsFalse(flush.IsCompleted);
        queue.EndSession();

        await Assert.ThrowsAsync<OperationCanceledException>(() => flush);
    }

    [TestMethod]
    public void OldSessionBatch_DoesNotReplayIntoNewSession()
    {
        ScrcpyControlCommandQueue queue = new();
        ScrcpyControlQueueSession oldSession = queue.StartSession();
        IControlMessage[] batch =
        [
            new KeycodeControlMessage
            {
                Action = AndroidKeyEventAction.AKEY_EVENT_ACTION_DOWN,
                KeyCode = AndroidKeycode.AKEYCODE_HOME
            },
            new KeycodeControlMessage
            {
                Action = AndroidKeyEventAction.AKEY_EVENT_ACTION_UP,
                KeyCode = AndroidKeycode.AKEYCODE_HOME
            }
        ];
        Assert.AreEqual(
            ScrcpyControlCommandQueueWriteResult.Accepted,
            queue.TryWriteBatchDetailed(batch));

        ScrcpyControlQueueSession newSession = queue.StartSession();
        Assert.IsFalse(newSession.TryRead(out _));
        Assert.IsTrue(oldSession.TryRead(out ScrcpyControlQueueItem? oldItem));
        ScrcpyControlCommandBatch oldBatch = ((ScrcpyControlQueueItem.Batch)oldItem!).Value;
        Assert.AreEqual(2, oldBatch.MessageCount);
    }

    [TestMethod]
    public void BatchMessageOrder_IsPreserved()
    {
        CollapsePanelsControlMessage first = new();
        ExpandSettingsPanelControlMessage second = new();
        BackOrScreenOnControlMessage third = new()
        {
            Action = AndroidKeyEventAction.AKEY_EVENT_ACTION_UP
        };
        ScrcpyControlCommandBatch batch = new([first, second, third]);

        CollectionAssert.AreEqual(
            new[]
            {
                ControlMessageType.CollapsePanels,
                ControlMessageType.ExpandSettingsPanel,
                ControlMessageType.BackOrScreenOn
            },
            batch.MessageTypes.ToArray());
    }

    [TestMethod]
    public void BatchSerialization_PreservesWireOrder()
    {
        ScrcpyControlCommandBatch batch = new(
        [
            new CollapsePanelsControlMessage(),
            new ExpandSettingsPanelControlMessage(),
            new CollapsePanelsControlMessage()
        ]);

        AssertBytes(
            new byte[] { 7, 6, 7 },
            Scrcpy.SerializeControlCommandBatch(batch));
    }

    [TestMethod]
    public async Task ControlWriter_StopBeforeNextBatch_DoesNotSendBufferedBatch()
    {
        ScrcpyControlCommandQueue queue = new();
        ScrcpyControlQueueSession session = queue.StartSession();
        ScrcpyControlCommandBatch first = new([new CollapsePanelsControlMessage()]);
        ScrcpyControlCommandBatch second = new([new ExpandSettingsPanelControlMessage()]);
        Assert.AreEqual(
            ScrcpyControlCommandQueueWriteResult.Accepted,
            queue.TryWriteBatchDetailed(first));
        Assert.AreEqual(
            ScrcpyControlCommandQueueWriteResult.Accepted,
            queue.TryWriteBatchDetailed(second));

        bool connected = true;
        List<ScrcpyControlCommandBatch> sent = [];
        await Scrcpy.RunControlWriterAsync(
            session,
            () => connected,
            (batch, _) =>
            {
                sent.Add(batch);
                connected = false;
                return ValueTask.CompletedTask;
            },
            CancellationToken.None);

        Assert.HasCount(1, sent);
        Assert.AreSame(first, sent[0]);

        ScrcpyControlQueueSession newSession = queue.StartSession();
        Assert.IsFalse(newSession.TryRead(out _));
    }

    [TestMethod]
    public async Task Writer_AwaitsAsyncBatchBeforeReadingNextItem()
    {
        ScrcpyControlCommandQueue queue = new();
        ScrcpyControlQueueSession session = queue.StartSession();
        ScrcpyControlCommandBatch first = new([new CollapsePanelsControlMessage()]);
        ScrcpyControlCommandBatch second = new([new ExpandSettingsPanelControlMessage()]);
        Assert.AreEqual(
            ScrcpyControlCommandQueueWriteResult.Accepted,
            queue.TryWriteBatchDetailed(first));
        Assert.AreEqual(
            ScrcpyControlCommandQueueWriteResult.Accepted,
            queue.TryWriteBatchDetailed(second));

        TaskCompletionSource firstStarted =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource releaseFirst =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource secondStarted =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        Task writer = Scrcpy.RunControlWriterAsync(
            session,
            static () => true,
            async (batch, cancellationToken) =>
            {
                if (ReferenceEquals(batch, first))
                {
                    firstStarted.TrySetResult();
                    await releaseFirst.Task.WaitAsync(cancellationToken);
                }
                else
                {
                    secondStarted.TrySetResult();
                }
            },
            CancellationToken.None);

        await firstStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));
        Assert.IsFalse(secondStarted.Task.IsCompleted);

        releaseFirst.TrySetResult();
        await secondStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));
        queue.EndSession();
        await writer.WaitAsync(TimeSpan.FromSeconds(1));
    }

    [TestMethod]
    public async Task FlushBarrier_CompletesAfterPreviousAsyncWrite()
    {
        ScrcpyControlCommandQueue queue = new();
        ScrcpyControlQueueSession session = queue.StartSession();
        ScrcpyControlCommandBatch first = new([new CollapsePanelsControlMessage()]);
        Assert.AreEqual(
            ScrcpyControlCommandQueueWriteResult.Accepted,
            queue.TryWriteBatchDetailed(first));

        TaskCompletionSource writeStarted =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource releaseWrite =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        Task writer = Scrcpy.RunControlWriterAsync(
            session,
            static () => true,
            async (_, cancellationToken) =>
            {
                writeStarted.TrySetResult();
                await releaseWrite.Task.WaitAsync(cancellationToken);
            },
            CancellationToken.None);

        await writeStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));
        Task flush = queue.FlushAsync();
        Assert.IsFalse(flush.IsCompleted);

        releaseWrite.TrySetResult();
        await flush.WaitAsync(TimeSpan.FromSeconds(1));
        queue.EndSession();
        await writer.WaitAsync(TimeSpan.FromSeconds(1));
    }

    [TestMethod]
    public async Task CanceledAsyncWrite_StopsWriter()
    {
        ScrcpyControlCommandQueue queue = new();
        ScrcpyControlQueueSession session = queue.StartSession();
        Assert.IsTrue(queue.TryWrite(new CollapsePanelsControlMessage()));
        using CancellationTokenSource cancellation = new();
        TaskCompletionSource writeStarted =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource pendingWrite =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        Task writer = Scrcpy.RunControlWriterAsync(
            session,
            static () => true,
            async (_, cancellationToken) =>
            {
                writeStarted.TrySetResult();
                await pendingWrite.Task.WaitAsync(cancellationToken);
            },
            cancellation.Token);

        await writeStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));
        cancellation.Cancel();
        await writer.WaitAsync(TimeSpan.FromSeconds(1));
        Assert.IsFalse(writer.IsFaulted);
        queue.EndSession();
    }

    [TestMethod]
    public async Task CanceledAsyncWrite_DoesNotReplayBatch()
    {
        ScrcpyControlCommandQueue queue = new();
        ScrcpyControlQueueSession session = queue.StartSession();
        ScrcpyControlCommandBatch first = new([new CollapsePanelsControlMessage()]);
        ScrcpyControlCommandBatch second = new([new ExpandSettingsPanelControlMessage()]);
        Assert.AreEqual(
            ScrcpyControlCommandQueueWriteResult.Accepted,
            queue.TryWriteBatchDetailed(first));
        Assert.AreEqual(
            ScrcpyControlCommandQueueWriteResult.Accepted,
            queue.TryWriteBatchDetailed(second));
        using CancellationTokenSource cancellation = new();
        TaskCompletionSource writeStarted =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource pendingWrite =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        int firstWriteCount = 0;
        int secondWriteCount = 0;
        Task writer = Scrcpy.RunControlWriterAsync(
            session,
            static () => true,
            async (batch, cancellationToken) =>
            {
                if (ReferenceEquals(batch, first))
                {
                    Interlocked.Increment(ref firstWriteCount);
                    writeStarted.TrySetResult();
                    await pendingWrite.Task.WaitAsync(cancellationToken);
                }
                else if (ReferenceEquals(batch, second))
                {
                    Interlocked.Increment(ref secondWriteCount);
                }
            },
            cancellation.Token);

        await writeStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));
        cancellation.Cancel();
        await writer.WaitAsync(TimeSpan.FromSeconds(1));

        Assert.AreEqual(1, firstWriteCount);
        Assert.AreEqual(0, secondWriteCount);
        queue.EndSession();
    }

    [TestMethod]
    public async Task SessionEnd_CancelsPendingAsyncWriter()
    {
        ScrcpyControlCommandQueue queue = new();
        ScrcpyControlQueueSession session = queue.StartSession();
        Assert.IsTrue(queue.TryWrite(new CollapsePanelsControlMessage()));
        TaskCompletionSource writeStarted =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource pendingWrite =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        Task writer = Scrcpy.RunControlWriterAsync(
            session,
            static () => true,
            async (_, cancellationToken) =>
            {
                writeStarted.TrySetResult();
                await pendingWrite.Task.WaitAsync(cancellationToken);
            },
            CancellationToken.None);

        await writeStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));
        session.End();
        await writer.WaitAsync(TimeSpan.FromSeconds(1));
        Assert.IsFalse(writer.IsFaulted);
    }

    [TestMethod]
    public async Task NewSession_DoesNotReceiveOldPartialBatch()
    {
        ScrcpyControlCommandQueue queue = new();
        ScrcpyControlQueueSession oldSession = queue.StartSession();
        ScrcpyControlCommandBatch oldBatch = new([new CollapsePanelsControlMessage()]);
        Assert.AreEqual(
            ScrcpyControlCommandQueueWriteResult.Accepted,
            queue.TryWriteBatchDetailed(oldBatch));
        TaskCompletionSource writeStarted =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource pendingWrite =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        int oldWriteCount = 0;
        Task oldWriter = Scrcpy.RunControlWriterAsync(
            oldSession,
            static () => true,
            async (batch, cancellationToken) =>
            {
                Assert.AreSame(oldBatch, batch);
                Interlocked.Increment(ref oldWriteCount);
                writeStarted.TrySetResult();
                await pendingWrite.Task.WaitAsync(cancellationToken);
            },
            CancellationToken.None);

        await writeStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));
        ScrcpyControlQueueSession newSession = queue.StartSession();

        await oldWriter.WaitAsync(TimeSpan.FromSeconds(1));
        Assert.AreEqual(1, oldWriteCount);
        Assert.IsFalse(newSession.TryRead(out _));
        queue.EndSession();
    }

    [TestMethod]
    public async Task ClipboardRetry_RetriesBusyClipboardWithBoundedDelaysThenStops()
    {
        int attempts = 0;
        int exhausted = 0;
        List<TimeSpan> delays = [];

        ExternalException exception = await Assert.ThrowsExactlyAsync<ExternalException>(() => ClipboardService.ExecuteWithBusyRetryAsync(
            () =>
            {
                attempts++;
                return Task.FromException<string?>(
                    new ExternalException("clipboard busy", unchecked((int)0x800401D0)));
            },
            CancellationToken.None,
            _ =>
            {
                exhausted++;
                return Task.CompletedTask;
            },
            (delay, _) =>
            {
                delays.Add(delay);
                return Task.CompletedTask;
            }));

        Assert.AreEqual("clipboard busy", exception.Message);
        Assert.AreEqual(3, attempts);
        Assert.AreEqual(1, exhausted);
        CollectionAssert.AreEqual(
            new[] { TimeSpan.FromMilliseconds(25), TimeSpan.FromMilliseconds(50) },
            delays);
    }

    [TestMethod]
    public async Task ClipboardRetry_ReturnsAfterBusyClipboardRecovers()
    {
        int attempts = 0;
        List<TimeSpan> delays = [];

        string? result = await ClipboardService.ExecuteWithBusyRetryAsync(
            () =>
            {
                attempts++;
                return attempts < 3
                    ? Task.FromException<string?>(
                        new ExternalException("clipboard busy", unchecked((int)0x800401D0)))
                    : Task.FromResult<string?>("ready");
            },
            CancellationToken.None,
            delay: (delay, _) =>
            {
                delays.Add(delay);
                return Task.CompletedTask;
            });

        Assert.AreEqual("ready", result);
        Assert.AreEqual(3, attempts);
        CollectionAssert.AreEqual(
            new[] { TimeSpan.FromMilliseconds(25), TimeSpan.FromMilliseconds(50) },
            delays);
    }

    [TestMethod]
    public void TouchDown_UsesScrcpyV123GoldenBytes()
    {
        TouchEventControlMessage message = new()
        {
            Action = AndroidMotionEventAction.AMOTION_EVENT_ACTION_DOWN,
            Position = CreatePosition(10, 20, 1080, 2220),
            Buttons = AndroidMotionEventButtons.AMOTION_EVENT_BUTTON_PRIMARY,
            Pressure = 1f
        };

        AssertBytes(
            new byte[]
            {
                2, 0,
                0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF,
                0, 0, 0, 10,
                0, 0, 0, 20,
                4, 0x38,
                8, 0xAC,
                0xFF, 0xFF,
                0, 0, 0, 1
            },
            message.ToBytes());
    }

    [TestMethod]
    public void TouchMove_UsesPrimaryButtonAndFullPressure()
    {
        TouchEventControlMessage message = new()
        {
            Action = AndroidMotionEventAction.AMOTION_EVENT_ACTION_MOVE,
            PointerId = 42,
            Position = CreatePosition(400, 500, 1080, 2220),
            Buttons = AndroidMotionEventButtons.AMOTION_EVENT_BUTTON_PRIMARY,
            Pressure = 1f
        };

        AssertBytes(
            new byte[]
            {
                2, 2,
                0, 0, 0, 0, 0, 0, 0, 42,
                0, 0, 1, 0x90,
                0, 0, 1, 0xF4,
                4, 0x38,
                8, 0xAC,
                0xFF, 0xFF,
                0, 0, 0, 1
            },
            message.ToBytes());
    }

    [TestMethod]
    public void TouchUp_UsesScrcpyV123GoldenBytes()
    {
        TouchEventControlMessage message = new()
        {
            Action = AndroidMotionEventAction.AMOTION_EVENT_ACTION_UP,
            PointerId = 42,
            Position = CreatePosition(-1, 2220, 400, 800),
            Buttons = 0,
            Pressure = 0f
        };

        AssertBytes(
            new byte[]
            {
                2, 1,
                0, 0, 0, 0, 0, 0, 0, 42,
                0xFF, 0xFF, 0xFF, 0xFF,
                0, 0, 8, 0xAC,
                1, 0x90,
                3, 0x20,
                0, 0,
                0, 0, 0, 0
            },
            message.ToBytes());
    }

    [TestMethod]
    public void TouchCancel_UsesNoButtonsAndZeroPressure()
    {
        TouchEventControlMessage message = new()
        {
            Action = AndroidMotionEventAction.AMOTION_EVENT_ACTION_CANCEL,
            PointerId = 42,
            Position = CreatePosition(100, 200, 400, 800),
            Buttons = 0,
            Pressure = 0f
        };

        AssertBytes(
            new byte[]
            {
                2, 3,
                0, 0, 0, 0, 0, 0, 0, 42,
                0, 0, 0, 100,
                0, 0, 0, 200,
                1, 0x90,
                3, 0x20,
                0, 0,
                0, 0, 0, 0
            },
            message.ToBytes());
    }

    [TestMethod]
    public void TouchPressure_UsesOfficialScrcpyV123FixedPointConversion()
    {
        Assert.AreEqual(0, TouchEventControlMessage.ToFixedPoint16(0f));
        Assert.AreEqual(32768, TouchEventControlMessage.ToFixedPoint16(0.5f));
        Assert.AreEqual(ushort.MaxValue, TouchEventControlMessage.ToFixedPoint16(1f));
    }

    [TestMethod]
    public void TouchPressure_RejectsValuesOutsideFixedPointRange()
    {
        TouchEventControlMessage message = new()
        {
            Pressure = 1.01f
        };

        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => message.ToBytes());
    }

    [TestMethod]
    public void Scroll_Uses25ByteScrcpyV123PacketAndSerializesButtons()
    {
        ScrollEventControlMessage message = new()
        {
            Position = CreatePosition(10, 20, 1080, 2220),
            HorizontalScroll = -2,
            VerticalScroll = 3,
            Buttons = AndroidMotionEventButtons.AMOTION_EVENT_BUTTON_SECONDARY
        };

        byte[] actual = message.ToBytes().ToArray();

        Assert.AreEqual(25, actual.Length);
        CollectionAssert.AreEqual(
            new byte[]
            {
                3,
                0, 0, 0, 10,
                0, 0, 0, 20,
                4, 0x38,
                8, 0xAC,
                0xFF, 0xFF, 0xFF, 0xFE,
                0, 0, 0, 3,
                0, 0, 0, 2
            },
            actual);
        CollectionAssert.AreEqual(new byte[] { 0, 0, 0, 2 }, actual[21..]);
    }

    [TestMethod]
    public void BackOrScreenOn_UsesScrcpyV123GoldenBytes()
    {
        BackOrScreenOnControlMessage message = new()
        {
            Action = AndroidKeyEventAction.AKEY_EVENT_ACTION_UP
        };

        AssertBytes(new byte[] { 4, 1 }, message.ToBytes());
    }

    [TestMethod]
    public void RotateDevice_UsesOneByteScrcpyV123Message()
    {
        AssertBytes(new byte[] { 11 }, new RotateDeviceControlMessage().ToBytes());
    }

    [TestMethod]
    public void SetScreenPowerMode_UsesV123OffAndNormalValues()
    {
        AssertBytes(
            new byte[] { 10, 2 },
            new SetScreenPowerModeControlMessage
            {
                Mode = AndroidScreenPowerMode.POWER_MODE_NORMAL
            }.ToBytes());
        AssertBytes(
            new byte[] { 10, 0 },
            new SetScreenPowerModeControlMessage
            {
                Mode = AndroidScreenPowerMode.POWER_MODE_OFF
            }.ToBytes());
    }

    [TestMethod]
    public void SingleViewServerArguments_ContainBoundedV123VideoProfile()
    {
        ViewDeviceLaunchOptions options = new("SERIAL")
        {
            MaxSize = 1280,
            MaxFps = 30,
            VideoBitRate = "4M"
        };

        string[] arguments = Scrcpy.BuildServerArguments(
                ScrcpyNetClientFactory.ParseBitrate(options.VideoBitRate),
                options.MaxSize,
                options.MaxFps)
            .ToArray();

        CollectionAssert.Contains(arguments, "1.23");
        CollectionAssert.Contains(arguments, "bit_rate=4000000");
        CollectionAssert.Contains(arguments, "max_size=1280");
        CollectionAssert.Contains(arguments, "max_fps=30");
        CollectionAssert.Contains(arguments, "control=true");
        CollectionAssert.Contains(arguments, "clipboard_autosync=true");
    }

    [TestMethod]
    public void ServerLifecycle_OnlyRemovesOwnedReverseEndpoint()
    {
        FakeAdbOperations adb = new();
        ScrcpyNetServerLifecycle lifecycle = new(CreateDevice(), adb);

        lifecycle.Setup(43123, "server.jar", CancellationToken.None);
        lifecycle.Cleanup();

        CollectionAssert.AreEqual(
            new[]
            {
                ScrcpyNetServerLifecycle.RemoteEndpoint,
                ScrcpyNetServerLifecycle.RemoteEndpoint
            },
            adb.RemovedReverseEndpoints);
        Assert.AreEqual("tcp:43123", adb.CreatedReverseLocal);
        Assert.AreEqual(ScrcpyNetServerLifecycle.RemoteEndpoint, adb.CreatedReverseRemote);
        Assert.AreEqual("server.jar", adb.UploadedServerFile);
    }

    [TestMethod]
    public void UniformContentRect_PillarboxAndLetterboxAreComputedFromSourceAspectRatio()
    {
        Rect pillarbox = ScrcpyDisplayGeometry.CalculateUniformContentRect(400, 800, 800, 800);
        Assert.AreEqual(new Rect(200, 0, 400, 800), pillarbox);

        Rect letterbox = ScrcpyDisplayGeometry.CalculateUniformContentRect(800, 400, 800, 800);
        Assert.AreEqual(new Rect(0, 200, 800, 400), letterbox);
    }

    [TestMethod]
    public void UniformMapping_CoversPortraitLandscapeAndTopLeftCases()
    {
        Assert.IsTrue(ScrcpyDisplayGeometry.TryMapPointToDevice(
            new WpfPoint(0, 0),
            1080,
            2220,
            540,
            1110,
            out WpfPoint portraitTopLeft));
        Assert.AreEqual(new WpfPoint(0, 0), portraitTopLeft);

        Assert.IsTrue(ScrcpyDisplayGeometry.TryMapPointToDevice(
            new WpfPoint(400, 400),
            400,
            800,
            800,
            800,
            out WpfPoint portraitSquareCenter));
        Assert.AreEqual(new WpfPoint(200, 400), portraitSquareCenter);

        Assert.IsTrue(ScrcpyDisplayGeometry.TryMapPointToDevice(
            new WpfPoint(400, 400),
            800,
            400,
            800,
            800,
            out WpfPoint landscapeSquareCenter));
        Assert.AreEqual(new WpfPoint(400, 200), landscapeSquareCenter);
    }

    [TestMethod]
    public void UniformMapping_MapsCenterAndClampsBottomRight()
    {
        Assert.IsTrue(ScrcpyDisplayGeometry.TryMapPointToDevice(
            new WpfPoint(400, 400),
            1920,
            1080,
            800,
            800,
            out WpfPoint center));
        Assert.AreEqual(new WpfPoint(960, 540), center);

        Assert.IsTrue(ScrcpyDisplayGeometry.TryMapPointToDevice(
            new WpfPoint(800, 600),
            800,
            400,
            800,
            800,
            out WpfPoint bottomRight));
        Assert.AreEqual(new WpfPoint(799, 399), bottomRight);
    }

    [TestMethod]
    public void UniformMapping_RejectsBlackBarsOnAllSides()
    {
        Assert.IsFalse(Maps(0, 400, 400, 800, 800, 800));
        Assert.IsFalse(Maps(800, 400, 400, 800, 800, 800));
        Assert.IsFalse(Maps(400, 0, 800, 400, 800, 800));
        Assert.IsFalse(Maps(400, 800, 800, 400, 800, 800));
    }

    [TestMethod]
    public void UniformMapping_HandlesRotatedPortraitDimensions()
    {
        Assert.IsTrue(ScrcpyDisplayGeometry.TryMapPointToDevice(
            new WpfPoint(400, 400),
            2220,
            1080,
            800,
            800,
            out WpfPoint mapped));
        Assert.AreEqual(new WpfPoint(1110, 540), mapped);
    }

    [TestMethod]
    public void PointerState_EmitsOneRecoveryUpForEachDown()
    {
        ScrcpyDisplayPointerState state = new();

        Assert.IsFalse(state.TryBeginPointerDown(captureSucceeded: false));
        Assert.IsFalse(state.IsPointerDown);
        Assert.IsTrue(state.TryBeginPointerDown(captureSucceeded: true));
        Assert.IsFalse(state.TryBeginPointerDown(captureSucceeded: true));
        Assert.IsTrue(state.EndPointerUp());
        Assert.IsFalse(state.EndPointerUp());
        Assert.IsTrue(state.BeginPointerDown());
        Assert.IsTrue(state.CanMove);
        Assert.IsTrue(state.ReleaseAfterCaptureLoss());
        Assert.IsFalse(state.ReleaseAfterCaptureLoss());
        Assert.IsFalse(state.CanMove);
    }

    [TestMethod]
    public void KeycodeHelper_MapsCommonPunctuationToAndroidKeycodes()
    {
        (Key WpfKey, AndroidKeycode AndroidKeycode)[] mappings =
        [
            (Key.OemComma, AndroidKeycode.AKEYCODE_COMMA),
            (Key.OemPeriod, AndroidKeycode.AKEYCODE_PERIOD),
            (Key.Oem2, AndroidKeycode.AKEYCODE_SLASH),
            (Key.Oem1, AndroidKeycode.AKEYCODE_SEMICOLON),
            (Key.OemQuotes, AndroidKeycode.AKEYCODE_APOSTROPHE),
            (Key.OemMinus, AndroidKeycode.AKEYCODE_MINUS),
            (Key.OemPlus, AndroidKeycode.AKEYCODE_EQUALS),
            (Key.OemOpenBrackets, AndroidKeycode.AKEYCODE_LEFT_BRACKET),
            (Key.OemCloseBrackets, AndroidKeycode.AKEYCODE_RIGHT_BRACKET),
            (Key.Escape, AndroidKeycode.AKEYCODE_ESCAPE),
            (Key.Home, AndroidKeycode.AKEYCODE_MOVE_HOME),
            (Key.End, AndroidKeycode.AKEYCODE_MOVE_END),
            (Key.PageUp, AndroidKeycode.AKEYCODE_PAGE_UP),
            (Key.PageDown, AndroidKeycode.AKEYCODE_PAGE_DOWN)
        ];

        foreach ((Key wpfKey, AndroidKeycode androidKeycode) in mappings)
            Assert.AreEqual(androidKeycode, KeycodeHelper.ConvertKey(wpfKey));
    }

    private static TouchEventControlMessage CreateTouchMessage(
        AndroidMotionEventAction action,
        ulong pointerId,
        int x,
        int y)
    {
        return new TouchEventControlMessage
        {
            Action = action,
            PointerId = pointerId,
            Position = CreatePosition(x, y, 1080, 2220),
            Buttons = ScrcpyDisplayTouchPolicy.GetButtons(action, pointerId),
            Pressure = ScrcpyDisplayTouchPolicy.GetPressure(action)
        };
    }

    private static void FillDroppableSoftLimit(ScrcpyControlCommandQueue queue)
    {
        for (int i = 0; i < ScrcpyControlCommandQueue.DroppableSoftLimit; i++)
            Assert.IsTrue(queue.TryWrite(CreateMoveMessage(i)));
    }

    private static KeycodeControlMessage CreateKeyMessage(
        AndroidKeyEventAction action,
        AndroidKeycode keyCode = AndroidKeycode.AKEYCODE_A)
    {
        return new KeycodeControlMessage
        {
            Action = action,
            KeyCode = keyCode
        };
    }

    private static ScrollEventControlMessage CreateMoveMessage(int value)
    {
        return new ScrollEventControlMessage
        {
            Position = CreatePosition(value, value, 1080, 2220)
        };
    }

    private static Position CreatePosition(int x, int y, ushort width, ushort height)
    {
        return new Position
        {
            Point = new ScrcpyNet.Point { X = x, Y = y },
            ScreenSize = new ScreenSize { Width = width, Height = height }
        };
    }

    private static bool Maps(
        double x,
        double y,
        int sourceWidth,
        int sourceHeight,
        double targetWidth,
        double targetHeight)
    {
        return ScrcpyDisplayGeometry.TryMapPointToDevice(
            new WpfPoint(x, y),
            sourceWidth,
            sourceHeight,
            targetWidth,
            targetHeight,
            out _);
    }

    private static async Task<ScrcpyControlQueueItem> ReadNextQueueItemAsync(
        ScrcpyControlQueueSession session)
    {
        while (await session.WaitToReadAsync().ConfigureAwait(false))
        {
            if (session.TryRead(out ScrcpyControlQueueItem? item))
                return item!;
        }

        throw new InvalidOperationException("The control queue ended before the expected item arrived.");
    }

    private static async Task<ScrcpyControlQueueItem.FlushBarrier> ReadFlushBarrierAsync(
        ScrcpyControlQueueSession session)
    {
        while (await session.WaitToReadAsync().ConfigureAwait(false))
        {
            while (session.TryRead(out ScrcpyControlQueueItem? item))
            {
                if (item is ScrcpyControlQueueItem.FlushBarrier barrier)
                    return barrier;
            }
        }

        throw new InvalidOperationException("The control queue ended before the flush barrier arrived.");
    }

    private static void AssertBytes(byte[] expected, Span<byte> actual)
    {
        CollectionAssert.AreEqual(expected, actual.ToArray());
    }

    private static DeviceData CreateDevice()
    {
        return (DeviceData)RuntimeHelpers.GetUninitializedObject(typeof(DeviceData));
    }

    private static byte[] BuildDeviceClipboardMessage(string text)
    {
        byte[] payload = Encoding.UTF8.GetBytes(text);
        byte[] message = new byte[1 + sizeof(uint) + payload.Length];
        message[0] = (byte)ScrcpyDeviceMessageType.Clipboard;
        BinaryPrimitives.WriteUInt32BigEndian(message.AsSpan(1), (uint)payload.Length);
        payload.CopyTo(message, 1 + sizeof(uint));
        return message;
    }

    private sealed class FragmentedReadStream : MemoryStream
    {
        private readonly int _maxChunk;

        public FragmentedReadStream(byte[] buffer, int maxChunk)
            : base(buffer, writable: false)
        {
            _maxChunk = maxChunk;
        }

        public override Task<int> ReadAsync(
            byte[] buffer,
            int offset,
            int count,
            CancellationToken cancellationToken)
        {
            return base.ReadAsync(
                buffer,
                offset,
                Math.Min(count, _maxChunk),
                cancellationToken);
        }
    }

    private sealed class FakeAdbOperations : IScrcpyNetAdbOperations
    {
        public List<string> RemovedReverseEndpoints { get; } = [];
        public string? CreatedReverseRemote { get; private set; }
        public string? CreatedReverseLocal { get; private set; }
        public string? UploadedServerFile { get; private set; }

        public void CreateReverseForward(DeviceData device, string remote, string local, bool rebind)
        {
            CreatedReverseRemote = remote;
            CreatedReverseLocal = local;
        }

        public void RemoveReverseForward(DeviceData device, string remote)
        {
            RemovedReverseEndpoints.Add(remote);
        }

        public Task ExecuteRemoteCommandAsync(
            string command,
            DeviceData device,
            IShellOutputReceiver receiver,
            CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public void UploadMobileServer(
            DeviceData device,
            string serverFile,
            string remotePath,
            CancellationToken cancellationToken)
        {
            UploadedServerFile = serverFile;
        }
    }
}
