namespace ScrcpyNet.Wpf;

internal sealed class ScrcpyDisplayPointerState
{
    public bool IsPointerDown { get; private set; }
    public bool IsPinchActive { get; private set; }

    public bool BeginPointerDown()
    {
        return TryBeginPointerDown(captureSucceeded: true, pinchRequested: false);
    }

    public bool TryBeginPointerDown(
        bool captureSucceeded,
        bool pinchRequested = false)
    {
        // A DOWN is committed only after WPF confirms mouse capture.
        if (!captureSucceeded || IsPointerDown)
            return false;

        IsPointerDown = true;
        IsPinchActive = pinchRequested;
        return true;
    }

    public bool CanMove => IsPointerDown;

    public bool EndPointerUp()
    {
        return ReleasePointer();
    }

    public bool ReleaseAfterCaptureLoss()
    {
        return ReleasePointer();
    }

    private bool ReleasePointer()
    {
        if (!IsPointerDown)
            return false;

        IsPointerDown = false;
        IsPinchActive = false;
        return true;
    }
}
