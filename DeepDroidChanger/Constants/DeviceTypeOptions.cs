namespace DeepDroidChanger.Constants
{
    public static class DeviceTypeOptions
    {
        public const string Sargo = "sargo";
        public const string Starlte = "starlte";
        public const string Tissot = "tissot";
        public const string Unknown = "unknown";

        public static readonly IReadOnlyList<string> All = new[]
        {
            Sargo,
            Starlte,
            Tissot,
            Unknown
        };
    }
}
