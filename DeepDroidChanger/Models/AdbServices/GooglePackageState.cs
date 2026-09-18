namespace DeepDroidChanger.Models;

public sealed record GooglePackageState(
    bool IsGmsDisabled,
    bool IsPlayStoreDisabled,
    bool IsGmsInstalled = true,
    bool IsPlayStoreInstalled = true);
