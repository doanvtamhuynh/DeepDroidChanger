using System.Collections.ObjectModel;

namespace ScrcpyNet;

/// <summary>
/// One logical control action admitted to the session queue as an atomic unit.
/// The wire payload is frozen before the batch can be queued.
/// </summary>
internal sealed class ScrcpyControlCommandBatch
{
    public ScrcpyControlCommandBatch(IReadOnlyList<IControlMessage> messages)
    {
        ArgumentNullException.ThrowIfNull(messages);
        if (messages.Count == 0)
            throw new ArgumentException("A control batch must contain at least one message.", nameof(messages));

        byte[][] serializedMessages = new byte[messages.Count][];
        ControlMessageType[] messageTypes = new ControlMessageType[messages.Count];
        bool isDroppable = true;
        int totalLength = 0;
        for (int i = 0; i < messages.Count; i++)
        {
            IControlMessage message = messages[i]
                ?? throw new ArgumentException("A control batch cannot contain null messages.", nameof(messages));

            messageTypes[i] = message.Type;
            isDroppable &= ScrcpyControlMessagePolicy.IsDroppable(message);
            byte[] serialized = message.ToBytes().ToArray();
            serializedMessages[i] = serialized;
            totalLength = checked(totalLength + serialized.Length);
        }

        byte[] payload = new byte[totalLength];
        int offset = 0;
        foreach (byte[] serialized in serializedMessages)
        {
            serialized.CopyTo(payload, offset);
            offset = checked(offset + serialized.Length);
        }

        Payload = payload;
        MessageCount = messages.Count;
        MessageTypes = new ReadOnlyCollection<ControlMessageType>(messageTypes);
        IsDroppable = isDroppable;
    }

    public ReadOnlyMemory<byte> Payload { get; }

    public int MessageCount { get; }

    public IReadOnlyList<ControlMessageType> MessageTypes { get; }

    public bool IsDroppable { get; }
}
