using DeepDroidChanger.Models;
namespace DeepDroidChanger.Services
{
    public interface ICarrierDataService
    {
        Task<IReadOnlyList<CarrierProfile>> GetCarrierProfilesAsync(CancellationToken cancellationToken);
    }
}
