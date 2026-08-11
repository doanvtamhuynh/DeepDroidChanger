namespace DeepDroidChanger.Models;

public sealed record InstallPackageBatchRequest
{
    public InstallPackageBatchRequest(
        IReadOnlyList<string> filePaths,
        InstallPackageOptions options)
    {
        ArgumentNullException.ThrowIfNull(filePaths);
        ArgumentNullException.ThrowIfNull(options);

        FilePaths = Array.AsReadOnly(filePaths.ToArray());
        Options = options;
    }

    public IReadOnlyList<string> FilePaths { get; }
    public InstallPackageOptions Options { get; }
}
