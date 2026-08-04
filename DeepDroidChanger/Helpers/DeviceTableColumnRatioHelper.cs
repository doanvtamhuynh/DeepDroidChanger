namespace DeepDroidChanger.Helpers;

public static class DeviceTableColumnRatioHelper
{
    public static void Replace(
        Dictionary<string, double> target,
        IReadOnlyDictionary<string, double> source)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(source);

        Dictionary<string, double> copiedRatios = new(source, StringComparer.Ordinal);
        target.Clear();
        foreach ((string key, double value) in copiedRatios)
            target[key] = value;
    }
}
