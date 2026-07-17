namespace DeepDroidChanger.Models;

public sealed class PackageListScopeOption
{
    public PackageListScopeOption(PackageListScope scope, string displayName)
    {
        Scope = scope;
        DisplayName = displayName;
    }

    public PackageListScope Scope { get; }

    public string DisplayName { get; }
}
