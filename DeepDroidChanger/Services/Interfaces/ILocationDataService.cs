using DeepDroidChanger.Models;

namespace DeepDroidChanger.Services
{
    public interface ILocationDataService
    {
        Task<IReadOnlyList<LocationOption>> GetLocationsAsync(CancellationToken cancellationToken);

        Task<IReadOnlyList<TimezoneOption>> GetTimezonesAsync(CancellationToken cancellationToken);
    }
}
