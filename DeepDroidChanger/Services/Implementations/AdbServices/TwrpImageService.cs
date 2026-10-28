using System.IO;
using System.Security.Cryptography;
using DeepDroidChanger.Models;

namespace DeepDroidChanger.Services;

/// <summary>Opens a verified recovery image without modifying or flashing it.</summary>
public sealed class TwrpImageService
{
    private readonly string _assets;
    public TwrpImageService() : this(Path.Combine(AppContext.BaseDirectory, "Assets", "TWRP")) { }
    internal TwrpImageService(string assets) => _assets = assets;
    public string? GetDefaultImagePath(string deviceMarker)
    {
        var image = TwrpRecoveryImage.Available.FirstOrDefault(item => item.Device == deviceMarker);
        if (image is null) return null;
        string path = Path.Combine(_assets, image.FileName);
        return File.Exists(path) ? path : null;
    }
    internal async Task<FileStream> OpenBootSourceAsync(string deviceMarker, CancellationToken token, string? sourceImagePath = null)
    {
        string expectedHash = deviceMarker switch
        {
            "sargo" => TwrpSafety.SourceHash,
            "tissot" => "0aeb095960e6f1c6e2ed350ab8cbb8fa289204a8e473708ce32b5e35812f8d73",
            _ => throw new TwrpWorkflowException("Twrp_EntryUnsupportedDevice")
        };
        string path = sourceImagePath is null ? GetDefaultImagePath(deviceMarker) ?? throw new TwrpWorkflowException("Twrp_EntryInvalidImage") : Path.GetFullPath(sourceImagePath);
        var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, useAsync: true);
        try
        {
            if (Convert.ToHexStringLower(await SHA256.HashDataAsync(stream, token).ConfigureAwait(false)) != expectedHash)
                throw new TwrpWorkflowException("Twrp_EntryInvalidImage");
            return stream;
        }
        catch
        {
            await stream.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }
}
