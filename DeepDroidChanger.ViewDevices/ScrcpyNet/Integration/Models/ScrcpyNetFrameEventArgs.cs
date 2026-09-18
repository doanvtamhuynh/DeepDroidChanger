namespace DeepDroidChanger.ViewDevices.Models;

public sealed class ScrcpyNetFrameEventArgs(int width, int height) : EventArgs
{
    public int Width { get; } = width;
    public int Height { get; } = height;
}
