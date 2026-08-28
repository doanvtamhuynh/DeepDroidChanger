namespace DeepDroidChanger.ViewDevices.Models;

public sealed record ScrcpyRuntimeInfo(
    string RuntimeDirectory,
    string ExecutablePath,
    string ServerPath,
    string CanonicalAdbPath);
