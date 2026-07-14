namespace DeepDroidChanger.Models
{
    public sealed class InstallPackageResult
    {
        public InstallPackageResult(
            string filePath,
            bool success,
            string messageResourceKey,
            string? failureCode = null,
            params object[] messageArguments)
        {
            FilePath = filePath;
            Success = success;
            MessageResourceKey = messageResourceKey;
            FailureCode = failureCode;
            MessageArguments = messageArguments;
        }

        public string FilePath { get; }
        public bool Success { get; }
        public string MessageResourceKey { get; }
        public string? FailureCode { get; }
        public IReadOnlyList<object> MessageArguments { get; }
    }
}
