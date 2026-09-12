namespace ScrcpyNet.Wpf;

internal sealed class ScrcpyDisplayPointerState
{
    public bool IsPointerDown { get; private set; }

    public bool BeginPointerDown()
    {
        if (IsPointerDown)
            return false;

        IsPointerDown = true;
        return true;
    }

    public bool CanMove => IsPointerDown;

    public bool EndPointerUp()
    {
        if (!IsPointerDown)
            return false;

        IsPointerDown = false;
        return true;
    }

    public bool CancelPointer()
    {
        if (!IsPointerDown)
            return false;

        IsPointerDown = false;
        return true;
    }
}
