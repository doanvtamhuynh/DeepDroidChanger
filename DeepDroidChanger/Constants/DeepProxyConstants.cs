namespace DeepDroidChanger.Constants
{
    public static class DeepProxyConstants
    {
        public const string SocksProxyType = "Socks 5";
        public const string PackageName = "hev.sockstun";
        public const string ServiceComponent = "hev.sockstun/.TProxyService";
        public const string ServiceMainActivity = "hev.sockstun/.MainActivity";
        public const string ConnectAction = "hev.sockstun.CONNECT";
        public const string DisconnectAction = "hev.sockstun.DISCONNECT";
        public const string EstablishVpnServiceAppOp = "ESTABLISH_VPN_SERVICE";
        public const string BrowserLeaksUrl = "https://browserleaks.com/ip";
        public const string PublicIpCheckUrl = "https://api64.ipify.org";
        public const string CountryIpv4BlocksUrlFormat = "https://raw.githubusercontent.com/doanvtamhuynh/country-ip-blocks/master/ipv4/{0}.cidr";
        public const int RemoteRequestTimeoutSeconds = 15;
        public const string ProxyIpProperty = "persist.deepdroid.proxy.ip";
        public const string ProxyPortProperty = "persist.deepdroid.proxy.port";
        public const string ProxyUsernameProperty = "persist.deepdroid.proxy.username";
        public const string ProxyPasswordProperty = "persist.deepdroid.proxy.password";
        public const string InterfaceIpv4Property = "persist.deepdroid.interface.ipv4";
        public const string InterfaceIpv4PrefixProperty = "persist.deepdroid.interface.ipv4.prefix";
    }
}
