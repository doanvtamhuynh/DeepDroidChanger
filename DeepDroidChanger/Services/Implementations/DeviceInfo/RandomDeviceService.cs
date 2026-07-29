using DeepDroidChanger.Models;
using DeepDroidChanger.Authentication;

namespace DeepDroidChanger.Services
{
    public sealed class RandomDeviceService : IRandomDeviceService
    {
        private readonly IAuthenticationSessionService _authenticationSessionService;
        private readonly IDeviceRandomProfileService _deviceRandomProfileService;

        public RandomDeviceService(
            IAuthenticationSessionService authenticationSessionService,
            IDeviceRandomProfileService deviceRandomProfileService)
        {
            _authenticationSessionService = authenticationSessionService;
            _deviceRandomProfileService = deviceRandomProfileService;
        }

        public async Task<RandomDeviceResult> CreateRandomProfileAsync(
            RandomDeviceRequest request,
            CancellationToken cancellationToken)
        {
            if (_authenticationSessionService.CurrentSession == null)
                return new RandomDeviceResult(RandomDeviceStatus.LoginRequired, null);

            try
            {
                var profile = await _deviceRandomProfileService
                    .CreateRandomProfileAsync(request, cancellationToken)
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
