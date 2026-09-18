using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.Logging;

namespace DeepDroidChanger.Services;

public sealed class ClipboardService : IClipboardService, IViewDeviceClipboardService
{
    private const int ClipboardBusyHResult = unchecked((int)0x800401D0);
    private const int MaxBusyAttempts = 3;
    private static readonly TimeSpan[] BusyRetryDelays =
    [
        TimeSpan.FromMilliseconds(25),
        TimeSpan.FromMilliseconds(50)
    ];

    private readonly ILogger<ClipboardService> _logger;

    public ClipboardService(ILogger<ClipboardService> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public void SetText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        Clipboard.SetText(text);
    }

    public Task<string?> GetTextAsync(CancellationToken cancellationToken = default)
    {
        return ExecuteWithBusyRetryAsync(
            () => InvokeOnUiAsync(
                () => Clipboard.ContainsText() ? Clipboard.GetText() : null,
                cancellationToken),
            cancellationToken,
            LogBusyClipboardFailureAsync);
    }

    public async Task SetTextAsync(string text, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(text);

        _ = await ExecuteWithBusyRetryAsync(
                () => InvokeOnUiAsync(
                    () =>
                    {
                        Clipboard.SetText(text);
                        return true;
                    },
                    cancellationToken),
                cancellationToken,
                LogBusyClipboardFailureAsync)
            .ConfigureAwait(false);
    }

    internal static async Task<T?> ExecuteWithBusyRetryAsync<T>(
        Func<Task<T>> operation,
        CancellationToken cancellationToken,
        Func<Exception, Task>? onBusyFailure = null,
        Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        ArgumentNullException.ThrowIfNull(operation);
        delay ??= static (retryDelay, token) => Task.Delay(retryDelay, token);

        for (int attempt = 0; ; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                return await operation().ConfigureAwait(false);
            }
            catch (Exception exception) when (IsClipboardBusy(exception))
            {
                if (attempt >= BusyRetryDelays.Length)
                {
                    if (onBusyFailure is not null)
                        await onBusyFailure(exception).ConfigureAwait(false);
                    throw;
                }

                await delay(BusyRetryDelays[attempt], cancellationToken).ConfigureAwait(false);
            }
        }
    }

    internal static bool IsClipboardBusy(Exception exception)
    {
        return exception is ExternalException externalException &&
               externalException.ErrorCode == ClipboardBusyHResult;
    }

    private Task LogBusyClipboardFailureAsync(Exception exception)
    {
        _logger.LogWarning(
            exception,
            "Windows clipboard remained busy after {Attempts} attempts.",
            MaxBusyAttempts);
        return Task.CompletedTask;
    }

    private static Task<T> InvokeOnUiAsync<T>(
        Func<T> operation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);
        cancellationToken.ThrowIfCancellationRequested();

        Dispatcher dispatcher = Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
        if (dispatcher.CheckAccess())
            return Task.FromResult(operation());

        return dispatcher
            .InvokeAsync(operation, DispatcherPriority.DataBind, cancellationToken)
            .Task;
    }
}
