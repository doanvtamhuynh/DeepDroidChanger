namespace DeepDroidChanger.Models
{
    public sealed class AppSettings
    {
        public string Language { get; set; } = "en";
        public string Theme { get; set; } = "Dark";
        public bool SidebarCollapsed { get; set; }
        public Dictionary<string, double> SingleDeviceTableColumnRatios { get; set; } = new()
        {
            ["Index"] = 0.55,
            ["Selected"] = 0.55,
            ["Serial"] = 1.05,
            ["Name"] = 1.05,
            ["Type"] = 0.9,
            ["Active"] = 1.05,
            ["Status"] = 1.0,
            ["Process"] = 1.95
        };
        public Dictionary<string, double> MultipleDeviceTableColumnRatios { get; set; } = new()
        {
            ["Index"] = 0.55,
            ["Selected"] = 0.55,
            ["Serial"] = 1.05,
            ["Name"] = 1.05,
            ["Type"] = 0.9,
            ["Active"] = 1.05,
            ["Status"] = 1.0,
            ["Process"] = 1.95
        };
        public string SelectedSingleDeviceSerial { get; set; } = string.Empty;
        public List<string> SelectedMultipleDeviceSerials { get; set; } = [];
    }
}
