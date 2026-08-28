namespace DeepDroidChanger.ViewDevices.Models;

public sealed record ViewDeviceLaunchOptions(string Serial)
{
    public const int BalancedMaxSize = 1280;
    public const int BalancedMaxFps = 30;
    public const string BalancedVideoBitRate = "4M";

    public int MaxSize { get; init; } = BalancedMaxSize;
    public int MaxFps { get; init; } = BalancedMaxFps;
    public string VideoBitRate { get; init; } = BalancedVideoBitRate;
}
