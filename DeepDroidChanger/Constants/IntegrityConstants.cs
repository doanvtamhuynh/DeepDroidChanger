namespace DeepDroidChanger.Constants
{
    public static class IntegrityConstants
    {
        public const string PifUrl = "https://gitlab.com/doanvtamhuynh/database/-/raw/main/integrity_keybox_db/pif.json";
        public const string KeyboxUrl = "https://gitlab.com/doanvtamhuynh/database/-/raw/main/integrity_keybox_db/keybox.xml";
        public const int MaxPifBytes = 2 * 1024 * 1024;
        public const int MaxKeyboxBytes = 1024 * 1024;
        public const int DownloadTimeoutSeconds = 30;
        public const string KeyboxRootElement = "AndroidAttestation";
        public const string KeyboxElement = "Keybox";

        public const string Prop_PifPrefix = "persist.props.config.";
        public const string Prop_PifTags = Prop_PifPrefix + "TAGS";
        public const string Prop_PifType = Prop_PifPrefix + "TYPE";
        public const string Prop_PifBoard = Prop_PifPrefix + "BOARD";
        public const string Prop_PifBrand = Prop_PifPrefix + "BRAND";
        public const string Prop_PifDevice = Prop_PifPrefix + "DEVICE";
        public const string Prop_PifFingerprint = Prop_PifPrefix + "FINGERPRINT";
        public const string Prop_PifHardware = Prop_PifPrefix + "HARDWARE";
        public const string Prop_PifId = Prop_PifPrefix + "ID";
        public const string Prop_PifIncremental = Prop_PifPrefix + "INCREMENTAL";
        public const string Prop_PifManufacturer = Prop_PifPrefix + "MANUFACTURER";
        public const string Prop_PifModel = Prop_PifPrefix + "MODEL";
        public const string Prop_PifProduct = Prop_PifPrefix + "PRODUCT";
        public const string Prop_PifRelease = Prop_PifPrefix + "RELEASE";
        public const string Prop_PifSecurityPatch = Prop_PifPrefix + "SECURITY_PATCH";
        public const string Prop_PifSdkInt = Prop_PifPrefix + "SDK_INT";
        public const string Prop_PifDeviceInitialSdkInt = Prop_PifPrefix + "DEVICE_INITIAL_SDK_INT";
    }
}
