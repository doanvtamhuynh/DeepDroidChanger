using System.Windows.Input;

namespace ScrcpyNet.Wpf;

internal readonly record struct ActiveAndroidKey(
    Key PhysicalKey,
    AndroidKeycode AndroidKeyCode,
    AndroidMetastate LastMetaState,
    uint Repeat);

internal sealed class ScrcpyDisplayKeyState
{
    private readonly Dictionary<Key, ActiveAndroidKey> activeKeys = [];

    public int Count => activeKeys.Count;

    public uint GetRepeatForKeyDown(Key physicalKey, bool isRepeat)
    {
        if (!isRepeat ||
            !activeKeys.TryGetValue(physicalKey, out ActiveAndroidKey activeKey))
        {
            return 0;
        }

        return checked(activeKey.Repeat + 1);
    }

    public void TrackKeyDown(
        Key physicalKey,
        AndroidKeycode androidKeyCode,
        AndroidMetastate metaState,
        uint repeat)
    {
        activeKeys[physicalKey] = new ActiveAndroidKey(
            physicalKey,
            androidKeyCode,
            metaState,
            repeat);
    }

    public bool TryRemove(Key physicalKey, out ActiveAndroidKey activeKey)
    {
        if (!activeKeys.TryGetValue(physicalKey, out activeKey))
            return false;

        activeKeys.Remove(physicalKey);
        return true;
    }

    public IReadOnlyList<ActiveAndroidKey> SnapshotAndClear()
    {
        ActiveAndroidKey[] snapshot = activeKeys.Values.ToArray();
        activeKeys.Clear();
        return snapshot;
    }

    public void Clear()
    {
        activeKeys.Clear();
    }
}
