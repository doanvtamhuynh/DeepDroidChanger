namespace DeepDroidChanger.Models;

public sealed record InstallPackageSetResult(
    IReadOnlyList<InstallPackageResult> Results,
    int SuccessCount,
    int TotalCount,
    string MessageResourceKey,
    IReadOnlyList<object> MessageArguments);
