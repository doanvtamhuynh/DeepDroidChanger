using DeepDroidChanger.Models;

namespace DeepDroidChanger.Helpers;

public static class SimProfileHelper
{
    public static SimProfile? FromDeviceProfile(DeviceInfoApiDevice profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (string.IsNullOrWhiteSpace(profile.Iccid)
            || string.IsNullOrWhiteSpace(profile.Imsi)
            || string.IsNullOrWhiteSpace(profile.SimOperatorCountry)
            || string.IsNullOrWhiteSpace(profile.SimOperatorNumeric))
        {
            return null;
        }

        return new SimProfile
        {
            Iccid = profile.Iccid,
            Imsi = profile.Imsi,
            PhoneNumber = profile.SimPhoneNumber,
            OperatorName = profile.SimOperatorName,
            OperatorCountry = profile.SimOperatorCountry,
            OperatorNumeric = profile.SimOperatorNumeric
        };
    }
}
