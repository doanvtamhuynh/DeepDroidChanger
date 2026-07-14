namespace DeepDroidChanger.Services;

public interface IDeviceSelectionService
{
    string? FindSelectionSerial(
        string? targetSerial,
        IReadOnlyList<string> visibleSerials,
        IReadOnlyList<string> allSerials);
}
