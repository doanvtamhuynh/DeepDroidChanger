namespace DeepDroidChanger.Models
{
    public sealed class CarrierOption
    {
        public CarrierOption(string carrierName, string mcc, string mnc)
        {
            CarrierName = carrierName;
            Mcc = mcc;
            Mnc = mnc;
            DisplayName = string.IsNullOrWhiteSpace(mcc) && string.IsNullOrWhiteSpace(mnc)
                ? carrierName
                : $"{carrierName} (MCC {mcc} / MNC {mnc})";
        }

        public string CarrierName { get; }
        public string Mcc { get; }
        public string Mnc { get; }
        public string DisplayName { get; }
    }
}
