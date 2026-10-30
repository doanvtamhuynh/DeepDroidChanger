namespace DeepDroidChanger.Services;

public interface IViewMultipleDeviceArtifactService
{
    string CreateRunDirectory(string destinationDirectory);
    string CreateDevicePath(string runDirectory, int index, string serial, string fileName);
}
