
namespace DeepDroidChanger.Services
{
    public sealed class DeviceActionService : IDeviceActionService
    {
        private readonly IAdbCommandService _commandService;

        public DeviceActionService(IAdbCommandService commandService)
        {
            _commandService = commandService;
        }

        public Task RebootAsync(string serial, CancellationToken cancellationToken)
        {
            return _commandService.RebootAsync(serial, cancellationToken);
        }
    }
}
