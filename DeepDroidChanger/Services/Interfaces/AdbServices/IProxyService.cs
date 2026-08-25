namespace DeepDroidChanger.Services
{
    public interface IProxyService
    {
        Task StartProxyAsync(
            string serial,
            string host,
            int port,
            string username,
            string password,
            string proxyType,
            CancellationToken cancellationToken);

        Task WaitForInternetAndOpenBrowserLeaksAsync(
            string serial,
            CancellationToken cancellationToken);

        Task StopProxyAsync(string serial, CancellationToken cancellationToken);
    }
}
