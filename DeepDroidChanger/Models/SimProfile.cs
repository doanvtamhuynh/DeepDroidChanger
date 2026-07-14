namespace DeepDroidChanger.Models;

public sealed class SimProfile
{
    public string Imsi { get; init; } = string.Empty;
    public string Iccid { get; init; } = string.Empty;
    public string PhoneNumber { get; init; } = string.Empty;
    public string OperatorNumeric { get; init; } = string.Empty;
    public string OperatorCountry { get; init; } = string.Empty;
    public string OperatorName { get; init; } = string.Empty;
}
