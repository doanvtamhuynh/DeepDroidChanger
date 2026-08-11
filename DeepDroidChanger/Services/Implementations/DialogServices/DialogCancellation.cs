using System.Windows;

namespace DeepDroidChanger.Services;

internal static class DialogCancellation
{
    public static CancellationTokenRegistration RegisterClose(
        Window window,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(window);

        return cancellationToken.Register(static state =>
        {
            var target = (Window)state!;
            if (target.Dispatcher.HasShutdownStarted
                || target.Dispatcher.HasShutdownFinished)
            {
                return;
            }

            try
            {
                _ = target.Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (target.IsVisible)
                        target.Close();
                }));
            }
            catch (InvalidOperationException)
            {
                // The dispatcher can begin shutting down between the guard and BeginInvoke.
            }
        }, window);
    }
}
