namespace DeepDroidChanger.Services;

public interface IDeviceActionService
{
    Task RebootAsync(string serial, CancellationToken cancellationToken);
}
