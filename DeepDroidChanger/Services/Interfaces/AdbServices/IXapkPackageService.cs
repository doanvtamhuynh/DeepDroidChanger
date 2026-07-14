using DeepDroidChanger.Models;
namespace DeepDroidChanger.Services
{
    public interface IXapkPackageService
    {
        Task<XapkPackageInfo> ExtractAsync(
            string xapkPath,
            string outputDirectory,
            CancellationToken cancellationToken);
    }
}
