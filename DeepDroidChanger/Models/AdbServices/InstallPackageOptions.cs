namespace DeepDroidChanger.Models
{
    public sealed class InstallPackageOptions
    {
        public InstallPackageOptions(bool grantPermissions, bool allowDowngrade)
        {
            GrantPermissions = grantPermissions;
            AllowDowngrade = allowDowngrade;
        }

        public bool GrantPermissions { get; }
        public bool AllowDowngrade { get; }
    }
}
