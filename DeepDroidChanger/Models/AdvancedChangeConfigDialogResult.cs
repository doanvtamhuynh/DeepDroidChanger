namespace DeepDroidChanger.Models
{
    public sealed class AdvancedChangeConfigDialogResult
    {
        public DeviceChangeOptions Options { get; }
        public bool UseIntegritySecurityPatch { get; }

        public AdvancedChangeConfigDialogResult(
            DeviceChangeOptions options,
            bool useIntegritySecurityPatch)
        {
            Options = options ?? throw new ArgumentNullException(nameof(options));
            UseIntegritySecurityPatch = useIntegritySecurityPatch;
        }
    }
}
