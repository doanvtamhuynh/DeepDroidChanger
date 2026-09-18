using System.Runtime.CompilerServices;
using System.Threading.Channels;

namespace ScrcpyNet;

internal enum ScrcpyControlCommandQueueWriteResult
{
    Accepted,
    Unavailable,
    Full
}

internal abstract class ScrcpyControlQueueItem
{
    private ScrcpyControlQueueItem()
    {
    }

    internal sealed class Batch : ScrcpyControlQueueItem
    {
        public Batch(ScrcpyControlCommandBatch value)
        {
            Value = value ?? throw new ArgumentNullException(nameof(value));
        }

        public ScrcpyControlCommandBatch Value { get; }
    }

    internal sealed class FlushBarrier : ScrcpyControlQueueItem
    {
        public FlushBarrier(TaskCompletionSource completion)
        {
            Completion = completion ?? throw new ArgumentNullException(nameof(completion));
        }

        public TaskCompletionSource Completion { get; }
    }
}

internal sealed class ScrcpyControlQueueSession
{
    private readonly Channel<ScrcpyControlQueueItem> channel;
    private readonly SemaphoreSlim droppableSlots = new(
        ScrcpyControlCommandQueue.DroppableSoftLimit,
        ScrcpyControlCommandQueue.DroppableSoftLimit);
    private readonly CancellationTokenSource ended = new();
    private int isEnded;

    public ScrcpyControlQueueSession()
    {
        channel = System.Threading.Channels.Channel.CreateBounded<ScrcpyControlQueueItem>(
            new BoundedChannelOptions(ScrcpyControlCommandQueue.Capacity)
            {
                SingleReader = true,
                SingleWriter = false,
                FullMode = BoundedChannelFullMode.Wait,
                AllowSynchronousContinuations = false
            });
    }

    internal ChannelWriter<ScrcpyControlQueueItem> Writer => channel.Writer;

    public CancellationToken EndedToken => ended.Token;

    public bool IsEnded => Volatile.Read(ref isEnded) != 0;

    public void End()
    {
        if (Interlocked.Exchange(ref isEnded, 1) != 0)
            return;

        try
        {
            ended.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }

        channel.Writer.TryComplete();
    }

    internal bool TryAcquireDroppablePermit()
    {
        return droppableSlots.Wait(0);
    }

    internal void ReleaseDroppablePermit()
    {
        droppableSlots.Release();
    }

    internal bool TryRead(out ScrcpyControlQueueItem? item)
    {
        if (!channel.Reader.TryRead(out item))
            return false;

        OnDequeued(item);
        return true;
    }

    internal ValueTask<bool> WaitToReadAsync(CancellationToken cancellationToken = default)
    {
        return channel.Reader.WaitToReadAsync(cancellationToken);
    }

    internal async IAsyncEnumerable<ScrcpyControlQueueItem> ReadAllAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (ScrcpyControlQueueItem item in channel.Reader.ReadAllAsync(cancellationToken))
        {
            OnDequeued(item);
            yield return item;
        }
    }

    private void OnDequeued(ScrcpyControlQueueItem item)
    {
        if (item is ScrcpyControlQueueItem.Batch { Value.IsDroppable: true })
            ReleaseDroppablePermit();
    }
}

internal sealed class ScrcpyControlCommandQueue
{
    public const int Capacity = 64;
    public const int CriticalReserve = 8;
    public const int DroppableSoftLimit = Capacity - CriticalReserve;

    private readonly object gate = new();
    private ScrcpyControlQueueSession? activeSession;

    public ScrcpyControlQueueSession StartSession()
    {
        ScrcpyControlQueueSession nextSession = new();
        ScrcpyControlQueueSession? previousSession;
        lock (gate)
        {
            previousSession = activeSession;
            activeSession = nextSession;
        }

        // Replacing a session ends the old channel after it is detached, so
        // no command can be admitted into it after the transition gate opens.
        previousSession?.End();
        return nextSession;
    }

    public bool IsSessionActive
    {
        get
        {
            lock (gate)
                return activeSession is { IsEnded: false };
        }
    }

    public bool TryWrite(IControlMessage message)
    {
        return TryWriteDetailed(message) == ScrcpyControlCommandQueueWriteResult.Accepted;
    }

    public ScrcpyControlCommandQueueWriteResult TryWriteDetailed(IControlMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        return TryWriteBatchDetailed(new ScrcpyControlCommandBatch([message]));
    }

    public ScrcpyControlCommandQueueWriteResult TryWriteBatchDetailed(
        IReadOnlyList<IControlMessage> messages)
    {
        return TryWriteBatchDetailed(new ScrcpyControlCommandBatch(messages));
    }

    internal ScrcpyControlCommandQueueWriteResult TryWriteBatchDetailed(
        ScrcpyControlCommandBatch batch)
    {
        ArgumentNullException.ThrowIfNull(batch);
        lock (gate)
        {
            ScrcpyControlQueueSession? session = activeSession;
            if (session is null || session.IsEnded)
                return ScrcpyControlCommandQueueWriteResult.Unavailable;

            bool permitAcquired = false;
            if (batch.IsDroppable)
            {
                if (!session.TryAcquireDroppablePermit())
                    return ScrcpyControlCommandQueueWriteResult.Full;

                permitAcquired = true;
            }

            try
            {
                if (session.Writer.TryWrite(new ScrcpyControlQueueItem.Batch(batch)))
                    return ScrcpyControlCommandQueueWriteResult.Accepted;
            }
            catch
            {
                if (permitAcquired)
                    session.ReleaseDroppablePermit();
                throw;
            }

            if (permitAcquired)
                session.ReleaseDroppablePermit();

            return activeSession == session && !session.IsEnded
                ? ScrcpyControlCommandQueueWriteResult.Full
                : ScrcpyControlCommandQueueWriteResult.Unavailable;
        }
    }

    public async Task FlushAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        ScrcpyControlQueueSession session;
        lock (gate)
        {
            session = activeSession
                ?? throw new InvalidOperationException("There is no active scrcpy control session to flush.");
            if (session.IsEnded)
                throw new InvalidOperationException("The scrcpy control session is no longer active.");
        }

        using CancellationTokenSource linkedCancellation =
            CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                session.EndedToken);
        TaskCompletionSource completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        ScrcpyControlQueueItem.FlushBarrier barrier = new(completion);

        // Do not hold the queue gate while waiting for bounded-channel space.
        await session.Writer.WriteAsync(barrier, linkedCancellation.Token)
            .ConfigureAwait(false);
        await completion.Task.WaitAsync(linkedCancellation.Token).ConfigureAwait(false);
    }

    public void EndSession()
    {
        ScrcpyControlQueueSession? session;
        lock (gate)
        {
            session = activeSession;
            activeSession = null;
        }

        session?.End();
    }
}
