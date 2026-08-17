namespace DeepDroidChanger.Constants;

public static class AssetConstants
{
    public static class Data
    {
        public const string RootPath = "Assets/Data/";
        public const string Bip0039Path = RootPath + "bip0039.txt";
        public const string CarriersPath = RootPath + "carriers.json";
        public const string ImeiTacsPath = RootPath + "imei_tacs.json";
        public const string LocationTimezonesPath = RootPath + "location-timezones.json";
        public const string MacVendorsPath = RootPath + "mac_vendors.json";
        public const string NamesPath = RootPath + "names.txt";
    }

    public static class Icons
    {
        public const string EnglishFlag = "pack://application:,,,/Assets/Icons/flag_en.ico";
        public const string VietnameseFlag = "pack://application:,,,/Assets/Icons/flag_vn.ico";
    }

    public static class Tools
    {
        public const string RootRelativePath = "Assets/Tools";
        public const string PlatformToolsDirectoryName = "platform-tools";
        public const string AdbExecutableName = "adb.exe";
        public const string FastbootExecutableName = "fastboot.exe";
    }

    public static class RuntimeData
    {
        public const string AppSettingsDirectoryName = "AppSettings";
        public const string AppSettingsFileName = "app_settings.json";
        public const string LegacyDeviceManagerDirectoryName = "DeviceManager";
        public const string ChangeSingleDeviceDirectoryName = "ChangeSingleDevice";
        public const string ChangeMultipleDevicesDirectoryName = "ChangeMultipleDevices";
        public const string DevicesFileName = "devices.json";
        public const string LegacyMultipleDevicesDirectoryName = "multiple_devices";
        public const string MultipleDeviceChangeConfigFileName = "change_config.json";
        public const string RandomConfigFileName = "random_config.json";
        public const string ChangeOptionsConfigFileName = "change_options_config.json";
        public const string UpdateIntegrityConfigFileName = "update_integrity_config.json";
        public const string LocationConfigFileName = "location_config.json";
        public const string TimezoneConfigFileName = "timezone_config.json";
        public const string ProxyConfigFileName = "proxy_config.json";
    }

    public static class Localization
    {
        public const string BaseStrings = "/DeepDroidChanger;component/Resources/Strings/Strings.xaml";
        public const string VietnameseStrings = "/DeepDroidChanger;component/Resources/Strings/Strings.vi.xaml";
    }

    public static class Themes
    {
        public const string LightDictionary = "/DeepDroidChanger;component/Resources/Themes/Theme.Light.xaml";
        public const string DarkDictionary = "/DeepDroidChanger;component/Resources/Themes/Theme.Dark.xaml";
        public const string LightFileName = "Theme.Light.xaml";
        public const string DarkFileName = "Theme.Dark.xaml";
    }
}
