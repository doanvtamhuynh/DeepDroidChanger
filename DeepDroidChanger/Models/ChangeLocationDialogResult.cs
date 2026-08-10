namespace DeepDroidChanger.Models
{
    public sealed class ChangeLocationDialogResult
    {
        public ChangeLocationDialogResult(
            ChangeLocationMode mode,
            string latitude,
            string longitude,
            LocationOption? selectedLocation = null)
        {
            Mode = mode;
            Latitude = latitude ?? string.Empty;
            Longitude = longitude ?? string.Empty;
            SelectedLocation = selectedLocation;
        }

        public ChangeLocationMode Mode { get; }
        public string Latitude { get; }
        public string Longitude { get; }
        public LocationOption? SelectedLocation { get; }
    }
}
