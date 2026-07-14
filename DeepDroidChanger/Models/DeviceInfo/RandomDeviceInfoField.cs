namespace DeepDroidChanger.Models;

public sealed class RandomDeviceInfoField
{
    public RandomDeviceInfoField(string key, string label, string value)
    {
        Key = key;
        Label = label;
        Value = value;
    }

    public string Key { get; }
    public string Label { get; }
    public string Value { get; set; }
}
