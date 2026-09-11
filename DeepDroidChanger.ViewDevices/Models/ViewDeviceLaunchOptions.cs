namespace DeepDroidChanger.ViewDevices.Models;

public sealed record ViewDeviceLaunchOptions(string Serial)
{
    public const int BalancedMaxSize = 1280;
    public const int BalancedMaxFps = 30;
    public const string BalancedVideoBitRate = "4M";

    public const int MultiViewMaxSize = 720;
    public const int MultiViewMaxFps = 20;
    public const string MultiViewVideoBitRate = "2M";

    public int MaxSize { get; init; } = BalancedMaxSize;
    public int MaxFps { get; init; } = BalancedMaxFps;
    public string VideoBitRate { get; init; } = BalancedVideoBitRate;
    public bool NoControl { get; init; }
    public bool NoAudio { get; init; }

    public static ViewDeviceLaunchOptions ForMultiView(string serial)
    {
        return new ViewDeviceLaunchOptions(serial)
        {
            MaxSize = MultiViewMaxSize,
            MaxFps = MultiViewMaxFps,
            VideoBitRate = MultiViewVideoBitRate,
            NoControl = false,
            NoAudio = true
        };
    }
}
