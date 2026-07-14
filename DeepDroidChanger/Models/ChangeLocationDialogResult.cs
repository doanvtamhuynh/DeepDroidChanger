namespace DeepDroidChanger.Models
{
    public sealed class ChangeLocationDialogResult
    {
        public ChangeLocationDialogResult(ChangeLocationMode mode, string latitude, string longitude)
        {
            Mode = mode;
            Latitude = latitude;
            Longitude = longitude;
        }

        public ChangeLocationMode Mode { get; }
        public string Latitude { get; }
        public string Longitude { get; }
    }
}
