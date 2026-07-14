namespace DeepDroidChanger.Models
{
    public sealed class ChangeTimezoneDialogResult
    {
        public ChangeTimezoneDialogResult(ChangeTimezoneMode mode, string timezone)
        {
            Mode = mode;
            Timezone = timezone;
        }

        public ChangeTimezoneMode Mode { get; }
        public string Timezone { get; }
    }
}
