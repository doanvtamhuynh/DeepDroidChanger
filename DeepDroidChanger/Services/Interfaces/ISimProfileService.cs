using DeepDroidChanger.Models;

namespace DeepDroidChanger.Services;

public interface ISimProfileService
{
    SimProfile CreateRandomProfile(CarrierCountryOption? country, CarrierOption? carrier);
}
