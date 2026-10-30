namespace DeepDroidChanger.Models
{
    public sealed class RandomDeviceSelection
    {
        public RandomDeviceSelection(string brand, int sdk)
        {
            Brand = brand;
            Sdk = sdk;
        }

        public string Brand { get; }
        public int Sdk { get; }
        /// <summary>Optional exact Android release. SDK zero means any SDK.</summary>
        public string? Release { get; init; }
    }
}
