namespace DeepDroidChanger.Models
{
    public sealed class InstallPackageDialogResult
    {
        public InstallPackageDialogResult(int totalCount, int successCount, int failedCount, bool canceled)
        {
            TotalCount = totalCount;
            SuccessCount = successCount;
            FailedCount = failedCount;
            Canceled = canceled;
        }

        public int TotalCount { get; }
        public int SuccessCount { get; }
        public int FailedCount { get; }
        public bool Canceled { get; }
    }
}
