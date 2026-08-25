namespace DeepDroidChanger.Models;

public sealed class ProxyWorkflowResult
{
    public ProxyWorkflowResult(
        bool locationUpdateFailed,
        bool timezoneUpdateFailed,
        string appliedLatitude = "",
        string appliedLongitude = "",
        string appliedTimezone = "")
    {
        LocationUpdateFailed = locationUpdateFailed;
        TimezoneUpdateFailed = timezoneUpdateFailed;
        AppliedLatitude = appliedLatitude;
        AppliedLongitude = appliedLongitude;
        AppliedTimezone = appliedTimezone;
    }

    public bool LocationUpdateFailed { get; }
    public bool TimezoneUpdateFailed { get; }
    public bool IsSuccess => !LocationUpdateFailed && !TimezoneUpdateFailed;
    public string AppliedLatitude { get; }
    public string AppliedLongitude { get; }
    public string AppliedTimezone { get; }
}
