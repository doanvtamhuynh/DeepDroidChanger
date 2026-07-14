namespace DeepDroidChanger.Constants
{
    public static class DeviceTableColumnSettings
    {
        public const string Index = nameof(Index);
        public const string Selected = nameof(Selected);
        public const string Serial = nameof(Serial);
        public const string Name = nameof(Name);
        public const string Type = nameof(Type);
        public const string Active = nameof(Active);
        public const string Status = nameof(Status);
        public const string Process = nameof(Process);

        public static IReadOnlyDictionary<string, double> DefaultRatios { get; } = new Dictionary<string, double>
        {
            [Index] = 0.55,
            [Selected] = 0.55,
            [Serial] = 1.05,
            [Name] = 1.05,
            [Type] = 0.9,
            [Active] = 1.05,
            [Status] = 1.0,
            [Process] = 1.95
        };
    }
}
