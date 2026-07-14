namespace DeepDroidChanger.Constants
{
    public static class AdbInstallConstants
    {
        public const string ApkExtension = ".apk";
        public const string XapkExtension = ".xapk";
        public const string TempInstallDirectoryName = "DeepDroidChanger";
        public const string TempInstallSubdirectoryName = "Install";
        public const string XapkManifestFileName = "manifest.json";
        public const string AndroidObbRemoteDirectoryFormat = "/sdcard/Android/obb/{0}";
        public const string InstallApkArgumentsFormat = "install -r {0}{1}";
        public const string InstallMultipleArgumentsFormat = "install-multiple -r {0}";
        public const string AllowDowngradeArgument = "-d ";
        public const string PushArgumentsFormat = "push {0} {1}";
        public const string MakeDirectoryCommandFormat = "mkdir -p {0}";
        public const string SuccessOutputToken = "Success";
        public const string FailureOutputPrefix = "Failure [";
        public const string FailureOutputSuffix = "]";
        public const char FailureCodeDetailSeparator = ':';
        public const string PackageNameJsonProperty = "package_name";
        public const string AlternatePackageNameJsonProperty = "package";
        public const string AlreadyExistsFailureCode = "INSTALL_FAILED_ALREADY_EXISTS";
        public const string VersionDowngradeFailureCode = "INSTALL_FAILED_VERSION_DOWNGRADE";
        public const string InsufficientStorageFailureCode = "INSTALL_FAILED_INSUFFICIENT_STORAGE";
        public const string InvalidApkFailureCode = "INSTALL_FAILED_INVALID_APK";
        public const string NoMatchingAbisFailureCode = "INSTALL_FAILED_NO_MATCHING_ABIS";
        public const string MissingSharedLibraryFailureCode = "INSTALL_FAILED_MISSING_SHARED_LIBRARY";
    }
}
