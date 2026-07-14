namespace DeepDroidChanger.Models
{
    public sealed class FakeProxyDialogResult
    {
        public string Host { get; }
        public int Port { get; }
        public string Username { get; }
        public string Password { get; }
        public string ProxyType { get; }
        public bool ProxyChangeLocationByIp { get; }
        public bool ProxyChangeTimezoneByIp { get; }

        public FakeProxyDialogResult(
            string host,
            int port,
            string username,
            string password,
            string proxyType,
            bool proxyChangeLocationByIp,
            bool proxyChangeTimezoneByIp)
        {
            Host = host;
            Port = port;
            Username = username;
            Password = password;
            ProxyType = proxyType;
            ProxyChangeLocationByIp = proxyChangeLocationByIp;
            ProxyChangeTimezoneByIp = proxyChangeTimezoneByIp;
        }
    }
}
