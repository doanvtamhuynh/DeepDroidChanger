namespace DeepDroidChanger.Constants;

public static class DeviceChangeConstants
{
    public const string RootUserId = "0";
    public const string EnabledValue = "1";
    public const string DisabledValue = "0";
    public const string SecureSettingsNamespace = "secure";
    public const string GlobalSettingsNamespace = "global";
    public const string SystemSettingsNamespace = "system";
    public const string AndroidIdSetting = "android_id";
    public const string DeviceNameSetting = "device_name";
    public const string BluetoothNameSetting = "bluetooth_name";
    public const string BluetoothAddressSetting = "bluetooth_address";
    public const string BluetoothAddressValidSetting = "bluetooth_addr_valid";
    public const string WifiP2pDeviceNameSetting = "wifi_p2p_device_name";
    public const string RandomMacSetting = "non_persistent_mac_randomization_force_enabled";
    public const string ScreenTimeoutSetting = "screen_off_timeout";
    public const string ScreenTimeoutValue = "1800000";
    public const string RootIdentityCommand = "id -u";
    public const string DisableLockScreenCommand = "locksettings set-disabled true";
    public const string DisableBluetoothCommand = "svc bluetooth disable";
    public const string SyncCommand = "sync";
    public const string BootCompletedCommand = "getprop sys.boot_completed";
    public const string BootCompletedValue = "1";
    public const int BootCompletionPollAttempts = 90;
    public const int BootCompletionPollDelayMilliseconds = 2000;
}
