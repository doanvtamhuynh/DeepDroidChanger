using System.Text.Json.Serialization;

namespace DeepDroidChanger.Models;

public sealed class MultipleDeviceProxyConfig
{
    public List<string> Proxies { get; set; } = [];
    public string ProxyType { get; set; } = ProxyEndpoint.DefaultProxyType;
    public bool ChangeLocationByIp { get; set; } = true;
    public bool ChangeTimezoneByIp { get; set; } = true;

    [JsonConverter(typeof(JsonStringEnumConverter<ProxyAssignmentMode>))]
    public ProxyAssignmentMode AssignmentMode { get; set; } = ProxyAssignmentMode.OneToOne;

    public int RepeatCount { get; set; } = 1;

    [JsonConverter(typeof(JsonStringEnumConverter<ProxyRepeatPattern>))]
    public ProxyRepeatPattern RepeatPattern { get; set; } = ProxyRepeatPattern.Interleaved;
}
