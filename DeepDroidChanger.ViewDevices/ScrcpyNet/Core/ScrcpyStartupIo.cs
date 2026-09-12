using System.IO;

namespace ScrcpyNet;

internal static class ScrcpyStartupIo
{
    public static CancellationTokenSource CreateDeadline(
        long timeoutMs,
        CancellationToken callerToken)
    {
        if (timeoutMs <= 0)
            throw new ArgumentOutOfRangeException(nameof(timeoutMs));

        CancellationTokenSource deadline =
            CancellationTokenSource.CreateLinkedTokenSource(callerToken);
        deadline.CancelAfter(TimeSpan.FromMilliseconds(timeoutMs));
        return deadline;
    }

    public static async Task ReadExactAsync(
        Stream stream,
        byte[] buffer,
        int offset,
        int count,
        CancellationToken deadlineToken,
        CancellationToken callerToken,
        string stage)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(buffer);
        ArgumentException.ThrowIfNullOrWhiteSpace(stage);
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        if (offset > buffer.Length - count)
            throw new ArgumentException("The requested range is outside the buffer.");

        try
        {
            int position = offset;
            int remaining = count;
            while (remaining > 0)
            {
                int bytesRead = await stream
                    .ReadAsync(buffer, position, remaining, deadlineToken)
                    .ConfigureAwait(false);
                if (bytesRead == 0)
                {
                    throw new EndOfStreamException(
                        "The scrcpy socket closed before the expected data was received.");
                }

                position += bytesRead;
                remaining -= bytesRead;
            }
        }
        catch (OperationCanceledException) when (callerToken.IsCancellationRequested)
        {
            throw new OperationCanceledException(callerToken);
        }
        catch (OperationCanceledException) when (deadlineToken.IsCancellationRequested)
        {
            throw new TimeoutException($"Timed out while {stage}.");
        }
    }
}
