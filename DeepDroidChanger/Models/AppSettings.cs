using DeepDroidChanger.Constants;
namespace DeepDroidChanger.Models
{
    public sealed class AppSettings
    {
        public string Language { get; set; } = LanguageConstants.English;
        public string Theme { get; set; } = ThemeConstants.Dark;
        public bool SidebarCollapsed { get; set; }
        public Dictionary<string, double> DeviceTableColumnRatios { get; set; } = new(DeviceTableColumnSettings.DefaultRatios);
        public string SelectedDeviceSerial { get; set; } = string.Empty;
    }
}
