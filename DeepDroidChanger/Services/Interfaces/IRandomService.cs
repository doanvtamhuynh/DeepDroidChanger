namespace DeepDroidChanger.Services
{
    public interface IRandomService
    {
        int RandomInRange(int minValue, int maxValue);
        T PickRandom<T>(IReadOnlyList<T> values);
        string GetRandomLocalIp();
        string GetRandomHexString(int minimumLength);
        string GenerateImsi(string mcc, string mnc);
        string GenerateIccid(string countryCode, string mnc);
        string GeneratePhoneNumber();
        string GenerateName(bool requireSingle = false);
        string GenerateImei(string brand, string? preferredTac = null);
        string GenerateMacAddress();
        string GenerateWifiMacAddress(string brand);
    }
}
