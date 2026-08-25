namespace DeepDroidChanger.Models;

public sealed class ProxyEndpoint
{
    private const string SocksScheme = "socks5://";
    public const string DefaultProxyType = "Socks 5";

    public ProxyEndpoint(string host, int port, string username, string password)
    {
        Host = host;
        Port = port;
        Username = username;
        Password = password;
    }

    public string Host { get; }
    public int Port { get; }
    public string Username { get; }
    public string Password { get; }
    public string NormalizedText => string.IsNullOrEmpty(Username)
        ? $"{Host}:{Port}"
        : $"{Host}:{Port}:{Username}:{Password}";

    public static bool IsSupportedProxyType(string? proxyType)
    {
        return string.Equals(proxyType?.Trim(), DefaultProxyType, StringComparison.OrdinalIgnoreCase);
    }

    public static bool TryParse(string? input, out ProxyEndpoint? endpoint)
    {
        endpoint = null;
        string normalized = input?.Trim() ?? string.Empty;
        if (normalized.Length == 0)
            return false;

        if (normalized.StartsWith(SocksScheme, StringComparison.OrdinalIgnoreCase))
            normalized = normalized[SocksScheme.Length..];
        else if (normalized.Contains("://", StringComparison.Ordinal))
            return false;

        string host;
        string portText;
        string username;
        string password;
        int authSeparator = normalized.IndexOf('@');
        if (authSeparator >= 0)
        {
            if (authSeparator != normalized.LastIndexOf('@'))
                return false;

            string[] credentials = normalized[..authSeparator]
                .Split(':', StringSplitOptions.TrimEntries);
            string[] address = normalized[(authSeparator + 1)..]
                .Split(':', StringSplitOptions.TrimEntries);
            if (credentials.Length != 2 || address.Length != 2)
                return false;

            username = credentials[0];
            password = credentials[1];
            host = address[0];
            portText = address[1];
        }
        else
        {
            string[] parts = normalized.Split(':', StringSplitOptions.TrimEntries);
            if (parts.Length is not (2 or 4))
                return false;

            host = parts[0];
            portText = parts[1];
            username = parts.Length == 4 ? parts[2] : string.Empty;
            password = parts.Length == 4 ? parts[3] : string.Empty;
        }

        if (host.Length == 0
            || !int.TryParse(portText, out int port)
            || port is < 1 or > 65535
            || string.IsNullOrWhiteSpace(username) != string.IsNullOrWhiteSpace(password))
        {
            return false;
        }

        endpoint = new ProxyEndpoint(host, port, username, password);
        return true;
    }
}
