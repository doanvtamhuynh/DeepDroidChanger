namespace DeepDroidChanger.Services;

public interface IDeviceInfoCatalogService
{
    Task InitializeAsync(CancellationToken cancellationToken);
    IReadOnlyList<string> Brands { get; }
    IReadOnlyList<string> GetAndroidVersions(string? brand);
}
