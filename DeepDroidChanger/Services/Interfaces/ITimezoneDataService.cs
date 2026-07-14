using DeepDroidChanger.Models;

namespace DeepDroidChanger.Services
{
    public interface ITimezoneDataService
    {
        Task<IReadOnlyList<TimezoneOption>> GetTimezonesAsync(CancellationToken cancellationToken);
    }
}
