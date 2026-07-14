namespace DeepDroidChanger.Services
{
    public sealed class DeviceRandomApiException : Exception
    {
        public DeviceRandomApiException(string message)
            : base(message)
        {
        }

        public DeviceRandomApiException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }
}
