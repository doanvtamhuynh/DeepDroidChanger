namespace DeepDroidChanger.Constants
{
    public static class AdbToolConstants
    {
        public const string AdbExecutableName = "adb.exe";
        public const string FastbootExecutableName = "fastboot.exe";
        public const string ScrcpyExecutableName = "scrcpy.exe";
        public const string SerialSelectorArgument = "-s";
        public const string ToolsRootRelativePath = "Assets/Tools";
        public const string PlatformToolsDirectoryName = "platform-tools";
        public const string ViewScreenDirectoryName = "viewscreen";
        public const string ScrcpyWindowTitlePrefix = "DeepDroidChangerScrcpy";
        public const string ScrcpySerialArgument = "--serial";
        public const string ScrcpyWindowTitleArgument = "--window-title";
        public const string ScrcpyWindowBorderlessArgument = "--window-borderless";
        public const string ScrcpyNoAudioArgument = "--no-audio";

        public const string AdbRootCommand = "root";
        public const string AdbWaitForDeviceCommand = "wait-for-device";
        public const string AdbRebootCommand = "reboot";
    }
}
