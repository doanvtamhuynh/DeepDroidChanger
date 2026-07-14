using System.Text.Json.Serialization;

namespace DeepDroidChanger.Models
{
    public sealed class DeviceInfoApiDevice
    {
        [JsonPropertyName("model")]
        public string? Model { get; set; }

        [JsonPropertyName("gaid")]
        public string? Gaid { get; set; }

        [JsonPropertyName("board")]
        public string? Board { get; set; }

        [JsonPropertyName("baseband")]
        public string? Baseband { get; set; }

        [JsonPropertyName("securityPath")]
        public string? SecurityPatch { get; set; }

        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("fingerprint")]
        public string? Fingerprint { get; set; }

        [JsonPropertyName("buildDisplayId")]
        public string? BuildDisplayId { get; set; }

        [JsonPropertyName("manufacturer")]
        public string? Manufacturer { get; set; }

        [JsonPropertyName("buildDateUtc")]
        public string? BuildDateUtc { get; set; }

        [JsonPropertyName("hardware")]
        public string? Hardware { get; set; }

        [JsonPropertyName("imei")]
        public string? Imei { get; set; }

        [JsonPropertyName("gpu")]
        public string? Gpu { get; set; }

        [JsonPropertyName("imei1")]
        public string? Imei1 { get; set; }

        [JsonPropertyName("buildHost")]
        public string? BuildHost { get; set; }

        [JsonPropertyName("gsf")]
        public string? Gsf { get; set; }

        [JsonPropertyName("platform")]
        public string? Platform { get; set; }

        [JsonPropertyName("bootloader")]
        public string? Bootloader { get; set; }

        public string Brand { get; set; } = string.Empty;
        public string Product { get; set; } = string.Empty;
        public string Code { get; set; } = string.Empty;
        public string Release { get; set; } = string.Empty;
        public string Sdk { get; set; } = string.Empty;
        public string Serial { get; set; } = string.Empty;
        public string AndroidId { get; set; } = string.Empty;
        public string Imsi { get; set; } = string.Empty;
        public string Iccid { get; set; } = string.Empty;
        public string SimPhoneNumber { get; set; } = string.Empty;
        public string SimOperatorNumeric { get; set; } = string.Empty;
        public string SimOperatorCountry { get; set; } = string.Empty;
        public string SimOperatorName { get; set; } = string.Empty;
        public string WifiMacAddress { get; set; } = string.Empty;
        public string BluetoothMacAddress { get; set; } = string.Empty;
    }
}
