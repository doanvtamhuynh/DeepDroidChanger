using DeepDroidChanger.Models;

namespace DeepDroidChanger.Services
{
    public sealed class RandomDeviceService : IRandomDeviceService
    {
        private readonly IDeviceSessionService _deviceSessionService;
        private readonly IDeviceRandomProfileService _deviceRandomProfileService;

        public RandomDeviceService(
            IDeviceSessionService deviceSessionService,
            IDeviceRandomProfileService deviceRandomProfileService)
        {
            _deviceSessionService = deviceSessionService;
            _deviceRandomProfileService = deviceRandomProfileService;
        }

        public async Task<RandomDeviceResult> CreateRandomProfileAsync(
            RandomDeviceRequest request,
            CancellationToken cancellationToken)
        {
            var session = _deviceSessionService.CurrentSession;
            if (session == null)
                return new RandomDeviceResult(RandomDeviceStatus.LoginRequired, null);

            try
            {
                var profile = await _deviceRandomProfileService
                    .CreateRandomProfileAsync(session, request, cancellationToken)
                    .ConfigureAwait(false);

                return new RandomDeviceResult(RandomDeviceStatus.Created, profile);
            }
            catch (DeviceRandomApiException)
            {
                return new RandomDeviceResult(RandomDeviceStatus.Failed, null);
            }
        }
    }
}
