namespace DeepDroidChanger.Services;

public interface ISettingsOutputService
{
    string? GetFolder(bool screenshot);
    string? GetFilePath(bool screenshot, string suggestedName);
}
