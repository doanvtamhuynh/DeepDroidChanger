using DeepDroidChanger.Constants;
using DeepDroidChanger.Models;
using DeepDroidChanger.Services;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace DeepDroidChanger.Tests.Services.Implementations.AdbServices;

[TestClass]
public sealed class DeviceIntegrityServiceTests
{
    [TestMethod]
    public async Task ReadBoundedContentAsync_UnknownLengthOverLimit_StopsAndRejectsContent()
    {
        using var content = new StreamContent(new MemoryStream(new byte[11]));
        content.Headers.ContentLength = null;

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            DeviceIntegrityService.ReadBoundedContentAsync(content, 10, CancellationToken.None));
    }

    [TestMethod]
    public async Task ReadBoundedContentAsync_ExactLimit_ReturnsDecodedText()
    {
        using var content = new StringContent("1234567890");

        string result = await DeviceIntegrityService.ReadBoundedContentAsync(
            content,
            10,
            CancellationToken.None);

        Assert.AreEqual("1234567890", result);
    }

    [TestMethod]
    public async Task TryGetRandomSecurityPatchAsync_ValidServerData_ReturnsSelectedPatch()
    {
        const string pifJson = "[{\"SECURITY_PATCH\":\"2026-06-01\"},{\"SECURITY_PATCH\":\"2026-07-01\"}]";
        IRandomService random = Substitute.For<IRandomService>();
        random.PickRandom(Arg.Any<IReadOnlyList<Integrity>>())
            .Returns(callInfo => callInfo.Arg<IReadOnlyList<Integrity>>()[1]);
        var service = new DeviceIntegrityService(
            Substitute.For<IAdbCommandService>(),
            random,
            NullLogger<DeviceIntegrityService>.Instance,
            (_, _, _) => Task.FromResult(pifJson));

        string? result = await service.TryGetRandomSecurityPatchAsync(CancellationToken.None);

        Assert.AreEqual("2026-07-01", result);
    }

    [TestMethod]
    public async Task TryGetRandomSecurityPatchAsync_InvalidServerData_ReturnsNullForCallerFallback()
    {
        var service = new DeviceIntegrityService(
            Substitute.For<IAdbCommandService>(),
            Substitute.For<IRandomService>(),
            NullLogger<DeviceIntegrityService>.Instance,
            (_, _, _) => Task.FromResult("not-json"));

        string? result = await service.TryGetRandomSecurityPatchAsync(CancellationToken.None);

        Assert.IsNull(result);
    }

    [TestMethod]
    public async Task UpdateIntegrityAsync_LocalFile_AppliesValidatedPifWithoutNetwork()
    {
        string jsonPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.json");
        await File.WriteAllTextAsync(
            jsonPath,
            "{\"FINGERPRINT\":\"google/redfin/redfin:13/TQ3A/123456:user/release-keys\","
            + "\"SECURITY_PATCH\":\"2025-01-05\",\"MODEL\":\"Pixel 5\",\"MANUFACTURER\":\"Google\"}");
        try
        {
            IAdbCommandService adb = Substitute.For<IAdbCommandService>();
            IRandomService random = Substitute.For<IRandomService>();
            random.PickRandom(Arg.Any<IReadOnlyList<Integrity>>())
                .Returns(callInfo => callInfo.Arg<IReadOnlyList<Integrity>>()[0]);
            var service = new DeviceIntegrityService(
                adb,
                random,
                NullLogger<DeviceIntegrityService>.Instance);

            await service.UpdateIntegrityAsync("SERIAL", fromServer: false, jsonPath, CancellationToken.None);

            await adb.Received(1).SetPropertyAsync(
                "SERIAL", IntegrityConstants.Prop_PifFingerprint,
                "google/redfin/redfin:13/TQ3A/123456:user/release-keys",
                Arg.Any<CancellationToken>());
            await adb.Received(1).SetPropertyAsync(
                "SERIAL", IntegrityConstants.Prop_PifSecurityPatch, "2025-01-05",
                Arg.Any<CancellationToken>());
            await adb.Received(1).SetPropertyAsync(
                "SERIAL", IntegrityConstants.Prop_PifModel, "Pixel 5",
                Arg.Any<CancellationToken>());
        }
        finally
        {
            File.Delete(jsonPath);
        }
    }

    [TestMethod]
    public async Task UpdateIntegrityAsync_MissingLocalFile_DoesNotCallAdb()
    {
        IAdbCommandService adb = Substitute.For<IAdbCommandService>();
        var service = new DeviceIntegrityService(
            adb,
            Substitute.For<IRandomService>(),
            NullLogger<DeviceIntegrityService>.Instance);

        await Assert.ThrowsExactlyAsync<FileNotFoundException>(() =>
            service.UpdateIntegrityAsync("SERIAL", false, "missing-pif.json", CancellationToken.None));

        await adb.DidNotReceiveWithAnyArgs().SetPropertyAsync(default!, default!, default!, default);
    }

    [TestMethod]
    public async Task UpdateIntegrityAsync_ServerMode_UsesDownloadedPifWithoutLiveNetwork()
    {
        const string pifJson = "{\"FINGERPRINT\":\"google/redfin/redfin:13/TQ3A/123456:user/release-keys\","
            + "\"SECURITY_PATCH\":\"2025-01-05\",\"MODEL\":\"Pixel 5\"}";
        IAdbCommandService adb = Substitute.For<IAdbCommandService>();
        IRandomService random = Substitute.For<IRandomService>();
        random.PickRandom(Arg.Any<IReadOnlyList<Integrity>>())
            .Returns(callInfo => callInfo.Arg<IReadOnlyList<Integrity>>()[0]);
        var service = new DeviceIntegrityService(
            adb,
            random,
            NullLogger<DeviceIntegrityService>.Instance,
            (url, _, _) => Task.FromResult(url == IntegrityConstants.PifUrl ? pifJson : string.Empty));

        await service.UpdateIntegrityAsync("SERIAL", fromServer: true, jsonPath: null, CancellationToken.None);

        await adb.Received(1).SetPropertyAsync(
            "SERIAL",
            IntegrityConstants.Prop_PifModel,
            "Pixel 5",
            Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task UpdateKeyboxAsync_ServerMode_DeletesTemporaryDownloadAfterPush()
    {
        IAdbCommandService adb = Substitute.For<IAdbCommandService>();
        string? pushedLocalPath = null;
        adb.RunAdbAsync("SERIAL", Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                string arguments = callInfo.ArgAt<string>(1);
                int firstQuote = arguments.IndexOf('"');
                int secondQuote = arguments.IndexOf('"', firstQuote + 1);
                pushedLocalPath = arguments[(firstQuote + 1)..secondQuote];
                Assert.IsTrue(File.Exists(pushedLocalPath));
                return new CommandResult(0, string.Empty, string.Empty);
            });
        var service = new DeviceIntegrityService(
            adb,
            Substitute.For<IRandomService>(),
            NullLogger<DeviceIntegrityService>.Instance,
            (_, _, _) => Task.FromResult("<AndroidAttestation><Keybox/></AndroidAttestation>"));

        await service.UpdateKeyboxAsync("SERIAL", fromServer: true, keyboxPath: null, CancellationToken.None);

        Assert.IsNotNull(pushedLocalPath);
        Assert.IsFalse(File.Exists(pushedLocalPath));
    }

    [DataRow("<AndroidAttestation>")]
    [DataRow("<Root><Keybox/></Root>")]
    [DataRow("<!DOCTYPE foo [<!ENTITY xxe SYSTEM 'file:///secret'>]><AndroidAttestation><Keybox>&xxe;</Keybox></AndroidAttestation>")]
    [TestMethod]
    public async Task UpdateKeyboxAsync_InvalidOrUnsafeXml_DoesNotCallAdb(string xml)
    {
        IAdbCommandService adb = Substitute.For<IAdbCommandService>();
        var service = new DeviceIntegrityService(
            adb,
            Substitute.For<IRandomService>(),
            NullLogger<DeviceIntegrityService>.Instance,
            (_, _, _) => Task.FromResult(xml));

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            service.UpdateKeyboxAsync("SERIAL", fromServer: true, keyboxPath: null, CancellationToken.None));

        await adb.DidNotReceiveWithAnyArgs().RunAdbAsync(default!, default!, default);
    }

    [TestMethod]
    public async Task UpdateKeyboxAsync_OversizedDownload_DoesNotCallAdb()
    {
        IAdbCommandService adb = Substitute.For<IAdbCommandService>();
        string oversizedXml = $"<AndroidAttestation><Keybox>{new string('x', IntegrityConstants.MaxKeyboxBytes)}</Keybox></AndroidAttestation>";
        var service = new DeviceIntegrityService(
            adb,
            Substitute.For<IRandomService>(),
            NullLogger<DeviceIntegrityService>.Instance,
            (_, _, _) => Task.FromResult(oversizedXml));

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            service.UpdateKeyboxAsync("SERIAL", fromServer: true, keyboxPath: null, CancellationToken.None));

        await adb.DidNotReceiveWithAnyArgs().RunAdbAsync(default!, default!, default);
    }

    [TestMethod]
    public async Task UpdateIntegrityAsync_OversizedDownload_DoesNotCallAdb()
    {
        IAdbCommandService adb = Substitute.For<IAdbCommandService>();
        string oversizedJson = new string('x', IntegrityConstants.MaxPifBytes + 1);
        var service = new DeviceIntegrityService(
            adb,
            Substitute.For<IRandomService>(),
            NullLogger<DeviceIntegrityService>.Instance,
            (_, _, _) => Task.FromResult(oversizedJson));

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            service.UpdateIntegrityAsync("SERIAL", fromServer: true, jsonPath: null, CancellationToken.None));

        await adb.DidNotReceiveWithAnyArgs().SetPropertyAsync(default!, default!, default!, default);
    }
}
