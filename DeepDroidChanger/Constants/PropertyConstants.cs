namespace DeepDroidChanger.Constants;

public static class PropertyConstants
{
    public const string ReadOnlyPrefix = "ro.";
    public const string Timezone = "persist.sys.timezone";
    public const string DeepDroidDevice = "ro.deepdroid.device";
    public const string Latitude = "persist.props.config.location.latitude";
    public const string Longitude = "persist.props.config.location.longitude";

    public static class Runtime
    {
        public const string ProductBrand = "ro.product.brand";
        public const string ProductManufacturer = "ro.product.manufacturer";
        public const string ProductModel = "ro.product.model";
        public const string ProductDevice = "ro.product.device";
        public const string ProductName = "ro.product.name";
        public const string AndroidRelease = "ro.build.version.release";
        public const string BuildFingerprint = "ro.build.fingerprint";
        public const string BuildId = "ro.build.id";
        public const string SecurityPatch = "ro.build.version.security_patch";
    }

    public static class Spoof
    {
        private const string Prefix = "persist.props.config.";

        public const string BypassReadOnlyProperties = Prefix + "bypass.roprop.enabled";
        public const string ProductBrand = Prefix + "product.brand";
        public const string ProductDevice = Prefix + "product.device";
        public const string ProductManufacturer = Prefix + "product.manufacturer";
        public const string ProductModel = Prefix + "product.model";
        public const string ProductName = Prefix + "product.name";
        public const string BuildFingerprint = Prefix + "build.fingerprint";
        public const string BuildId = Prefix + "build.id";
        public const string BuildIncremental = Prefix + "build.version.incremental";
        public const string BuildDate = Prefix + "build.date";
        public const string BuildDateUtc = Prefix + "build.date.utc";
        public const string BuildUser = Prefix + "build.user";
        public const string BuildHost = Prefix + "build.host";
        public const string BuildFlavor = Prefix + "build.flavor";
        public const string BuildProduct = Prefix + "build.product";
        public const string Hardware = Prefix + "hardware";
        public const string Board = Prefix + "board";
        public const string Platform = Prefix + "platform";
        public const string Bootloader = Prefix + "bootloader";
        public const string SocManufacturer = Prefix + "soc.manufacturer";
        public const string SocModel = Prefix + "soc.model";
        public const string SecurityPatch = Prefix + "security_patch";
        public const string AndroidRelease = Prefix + "build.version.release";
        public const string BuildDisplayId = Prefix + "build.display.id";
        public const string BuildDescription = Prefix + "build.description";
        public const string ClientIdBase = Prefix + "clientidbase";
        public const string Baseband = Prefix + "gsm.version.baseband";
        public const string SerialNumber = Prefix + "serial_number";
        public const string DeviceName = Prefix + "device.name";
        public const string VbmetaDigest = Prefix + "vbmeta.digest";
        public const string Imei0 = Prefix + "imei0";
        public const string Imei1 = Prefix + "imei1";
        public const string BluetoothMac = Prefix + "bluetooth.mac";
        public const string BluetoothName = Prefix + "bluetooth.name";
        public const string WifiMac = Prefix + "wifi.mac";
        public const string WifiBssid = Prefix + "wifi.bssid";
        public const string WifiSsid = Prefix + "wifi.ssid";
        public const string SimEnabled = Prefix + "sim.enabled";
        public const string SimIccid = Prefix + "sim.iccid";
        public const string SimImsi = Prefix + "sim.imsi";
        public const string SimPhoneNumber = Prefix + "sim.phone_number";
        public const string SimOperatorName = Prefix + "sim.operator.name";
        public const string SimOperatorCountry = Prefix + "sim.operator.country";
        public const string SimOperatorNumeric = Prefix + "sim.operator.numeric";
        public const string Sim2Enabled = Prefix + "sim2.enabled";
        public const string Sim2Iccid = Prefix + "sim2.iccid";
        public const string Sim2Imsi = Prefix + "sim2.imsi";
        public const string Sim2PhoneNumber = Prefix + "sim2.phone_number";
    }

    public static class Integrity
    {
        private const string Prefix = "persist.props.config.";

        public const string Tags = Prefix + "TAGS";
        public const string Type = Prefix + "TYPE";
        public const string Board = Prefix + "BOARD";
        public const string Brand = Prefix + "BRAND";
        public const string Device = Prefix + "DEVICE";
        public const string Fingerprint = Prefix + "FINGERPRINT";
        public const string Hardware = Prefix + "HARDWARE";
        public const string Id = Prefix + "ID";
        public const string Incremental = Prefix + "INCREMENTAL";
        public const string Manufacturer = Prefix + "MANUFACTURER";
        public const string Model = Prefix + "MODEL";
        public const string Product = Prefix + "PRODUCT";
        public const string Release = Prefix + "RELEASE";
        public const string SecurityPatch = Prefix + "SECURITY_PATCH";
        public const string SdkInt = Prefix + "SDK_INT";
        public const string DeviceInitialSdkInt = Prefix + "DEVICE_INITIAL_SDK_INT";
    }

    public static class Proxy
    {
        public const string Ip = "persist.deepdroid.proxy.ip";
        public const string Port = "persist.deepdroid.proxy.port";
        public const string Username = "persist.deepdroid.proxy.username";
        public const string Password = "persist.deepdroid.proxy.password";
        public const string InterfaceIpv4 = "persist.deepdroid.interface.ipv4";
        public const string InterfaceIpv4Prefix = "persist.deepdroid.interface.ipv4.prefix";
    }
}
