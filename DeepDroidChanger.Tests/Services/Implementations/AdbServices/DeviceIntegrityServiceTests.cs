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
            ConfigureRootAccess(adb);
            IRandomService random = Substitute.For<IRandomService>();
            random.PickRandom(Arg.Any<IReadOnlyList<Integrity>>())
                .Returns(callInfo => callInfo.Arg<IReadOnlyList<Integrity>>()[0]);
            var service = new DeviceIntegrityService(
                adb,
                random,
                NullLogger<DeviceIntegrityService>.Instance);

            await service.UpdateIntegrityAsync("SERIAL", fromServer: false, jsonPath, CancellationToken.None);

            await adb.Received(1).SetPropertyAsync(
                "SERIAL", PropertyConstants.Integrity.Fingerprint,
                "google/redfin/redfin:13/TQ3A/123456:user/release-keys",
                Arg.Any<CancellationToken>());
            await adb.Received(1).SetPropertyAsync(
                "SERIAL", PropertyConstants.Integrity.SecurityPatch, "2025-01-05",
                Arg.Any<CancellationToken>());
            await adb.Received(1).SetPropertyAsync(
                "SERIAL", PropertyConstants.Integrity.Model, "Pixel 5",
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
        ConfigureRootAccess(adb);
        IRandomService random = Substitute.For<IRandomService>();
        random.PickRandom(Arg.Any<IReadOnlyList<Integrity>>())
            .Returns(callInfo => callInfo.Arg<IReadOnlyList<Integrity>>()[0]);
        var service = new DeviceIntegrityService(
            adb,
            random,
            NullLogger<DeviceIntegrityService>.Instance,
            (url, _, _) => Task.FromResult(url == UrlConstants.Pif ? pifJson : string.Empty));

        await service.UpdateIntegrityAsync("SERIAL", fromServer: true, jsonPath: null, CancellationToken.None);

        await adb.Received(1).SetPropertyAsync(
            "SERIAL",
            PropertyConstants.Integrity.Model,
            "Pixel 5",
            Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task UpdateKeyboxAsync_ServerMode_WhenIntegrityIsNotSelected_OnlyUpdatesKeybox()
    {
        IAdbCommandService adb = Substitute.For<IAdbCommandService>();
        ConfigureRootAccess(adb);
        string? pushedLocalPath = null;
        adb.PushFileAsync("SERIAL", Arg.Any<string>(), "/data/system/keybox.xml", Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                pushedLocalPath = callInfo.ArgAt<string>(1);
                Assert.IsTrue(File.Exists(pushedLocalPath));
                return Task.FromResult(new CommandResult(0, string.Empty, string.Empty));
            });
        var service = new DeviceIntegrityService(
            adb,
            Substitute.For<IRandomService>(),
            NullLogger<DeviceIntegrityService>.Instance,
            (_, _, _) => Task.FromResult("<AndroidAttestation><Keybox/></AndroidAttestation>"));

        await service.UpdateKeyboxAsync("SERIAL", fromServer: true, keyboxPath: null, CancellationToken.None);

        Assert.IsNotNull(pushedLocalPath);
        Assert.IsFalse(File.Exists(pushedLocalPath));
        await adb.Received(1).SetPropertyAsync(
            "SERIAL",
            PropertyConstants.Keybox.Enabled,
            "true",
            Arg.Any<CancellationToken>());
        await adb.DidNotReceive().SetPropertyAsync(
            "SERIAL",
            PropertyConstants.Integrity.SdkInt,
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
        await adb.DidNotReceive().SetPropertyAsync(
            "SERIAL",
            PropertyConstants.Integrity.Enabled,
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
        await adb.DidNotReceive().SetPropertyAsync(
            "SERIAL",
            PropertyConstants.Integrity.DroidGuardSdk,
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
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
        string oversizedXml = $"<AndroidAttestation><Keybox>{new string('x', 1024 * 1024)}</Keybox></AndroidAttestation>";
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
        string oversizedJson = new string('x', 2 * 1024 * 1024 + 1);
        var service = new DeviceIntegrityService(
            adb,
            Substitute.For<IRandomService>(),
            NullLogger<DeviceIntegrityService>.Instance,
            (_, _, _) => Task.FromResult(oversizedJson));

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            service.UpdateIntegrityAsync("SERIAL", fromServer: true, jsonPath: null, CancellationToken.None));

        await adb.DidNotReceiveWithAnyArgs().SetPropertyAsync(default!, default!, default!, default);
    }

    [TestMethod]
    public async Task ClearIntegrityAsync_DisablesOnlyIntegrityEnableProperties()
    {
        IAdbCommandService adb = Substitute.For<IAdbCommandService>();
        DeviceIntegrityService service = CreateService(adb);

        await service.ClearIntegrityAsync("SERIAL", CancellationToken.None);

        await adb.Received(1).SetPropertyAsync(
            "SERIAL",
            PropertyConstants.Integrity.Enabled,
            "false",
            Arg.Any<CancellationToken>());
        await adb.Received(1).SetPropertyAsync(
            "SERIAL",
            PropertyConstants.Integrity.DroidGuardSdk,
            "false",
            Arg.Any<CancellationToken>());
        await adb.Received(2).SetPropertyAsync(
            "SERIAL",
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
        await adb.DidNotReceive().SetPropertyAsync(
            "SERIAL",
            PropertyConstants.Integrity.SdkInt,
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
        await adb.DidNotReceive().SetPropertyAsync(
            "SERIAL",
            PropertyConstants.Integrity.Fingerprint,
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
        await adb.DidNotReceive().SetPropertyAsync(
            "SERIAL",
            PropertyConstants.Keybox.Enabled,
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task ClearKeyboxAsync_DisablesOnlyKeyboxProperty()
    {
        IAdbCommandService adb = Substitute.For<IAdbCommandService>();
        DeviceIntegrityService service = CreateService(adb);

        await service.ClearKeyboxAsync("SERIAL", CancellationToken.None);

        await adb.Received(1).SetPropertyAsync(
            "SERIAL",
            PropertyConstants.Keybox.Enabled,
            "false",
            Arg.Any<CancellationToken>());
        await adb.Received(1).SetPropertyAsync(
            "SERIAL",
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
        await adb.DidNotReceive().SetPropertyAsync(
            "SERIAL",
            PropertyConstants.Integrity.Enabled,
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
        await adb.DidNotReceive().SetPropertyAsync(
            "SERIAL",
            PropertyConstants.Integrity.DroidGuardSdk,
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task ApplyAsyncAndApplyPreparedAsync_WhenNoTargetsAreSelected_DoNotWriteProperties()
    {
        IAdbCommandService adb = Substitute.For<IAdbCommandService>();
        DeviceIntegrityService service = CreateService(adb);
        var result = new UpdateIntegrityDialogResult(
            updateIntegrityFromServer: false,
            updateIntegrityEnabled: false,
            updateKeyboxEnabled: false,
            updateIntegrityFile: string.Empty,
            updateKeyboxFile: string.Empty,
            fakeDroidGuardSdkEnabled: true);

        await service.ApplyAsync("SERIAL", result, CancellationToken.None);
        await service.ApplyPreparedAsync(
            "SERIAL",
            new PreparedIntegrityData(Array.Empty<Integrity>(), null, fakeDroidGuardSdkEnabled: true),
            CancellationToken.None);

        await adb.DidNotReceiveWithAnyArgs().SetPropertyAsync(default!, default!, default!, default);
        await adb.DidNotReceiveWithAnyArgs().RunAdbAsync(default!, default!, default);
    }

    [TestMethod]
    public async Task ApplyAsyncAndApplyPreparedAsync_WhenFakeSdkIsDisabled_SetFalseAndPreserveSdkInt()
    {
        IAdbCommandService adb = Substitute.For<IAdbCommandService>();
        DeviceIntegrityService service = CreateService(adb);
        string pifPath = await WritePifFileAsync("35");
        UpdateIntegrityDialogResult result = CreateIntegrityResult(pifPath, fakeDroidGuardSdkEnabled: false);

        try
        {
            await service.ApplyAsync("SERIAL", result, CancellationToken.None);
            await ApplyPreparedIntegrityAsync(service, result);

            await adb.Received(2).SetPropertyAsync(
                "SERIAL",
                PropertyConstants.Integrity.Enabled,
                "true",
                Arg.Any<CancellationToken>());
            await adb.Received(2).SetPropertyAsync(
                "SERIAL",
                PropertyConstants.Integrity.DroidGuardSdk,
                "false",
                Arg.Any<CancellationToken>());
            await adb.DidNotReceive().SetPropertyAsync(
                "SERIAL",
                PropertyConstants.Integrity.SdkInt,
                Arg.Any<string>(),
                Arg.Any<CancellationToken>());
            await adb.DidNotReceive().SetPropertyAsync(
                "SERIAL",
                PropertyConstants.Keybox.Enabled,
                Arg.Any<string>(),
                Arg.Any<CancellationToken>());
        }
        finally
        {
            File.Delete(pifPath);
        }
    }

    [TestMethod]
    public async Task ApplyAsyncAndApplyPreparedAsync_WhenFakeSdkIsEnabled_UsePifSdkIntOrFallback32()
    {
        IAdbCommandService adb = Substitute.For<IAdbCommandService>();
        DeviceIntegrityService service = CreateService(adb);
        string pifWithSdkIntPath = await WritePifFileAsync("35");
        string pifWithoutSdkIntPath = await WritePifFileAsync(null);
        string pifWithBlankSdkIntPath = await WritePifFileAsync("   ");

        try
        {
            UpdateIntegrityDialogResult singleResult =
                CreateIntegrityResult(pifWithSdkIntPath, fakeDroidGuardSdkEnabled: true);
            await service.ApplyAsync("SERIAL", singleResult, CancellationToken.None);

            UpdateIntegrityDialogResult batchResult =
                CreateIntegrityResult(pifWithoutSdkIntPath, fakeDroidGuardSdkEnabled: true);
            await ApplyPreparedIntegrityAsync(service, batchResult);

            UpdateIntegrityDialogResult batchBlankResult =
                CreateIntegrityResult(pifWithBlankSdkIntPath, fakeDroidGuardSdkEnabled: true);
            await ApplyPreparedIntegrityAsync(service, batchBlankResult);

            await adb.Received(1).SetPropertyAsync(
                "SERIAL",
                PropertyConstants.Integrity.SdkInt,
                "35",
                Arg.Any<CancellationToken>());
            await adb.Received(2).SetPropertyAsync(
                "SERIAL",
                PropertyConstants.Integrity.SdkInt,
                "32",
                Arg.Any<CancellationToken>());
            await adb.Received(3).SetPropertyAsync(
                "SERIAL",
                PropertyConstants.Integrity.DroidGuardSdk,
                "true",
                Arg.Any<CancellationToken>());
        }
        finally
        {
            File.Delete(pifWithSdkIntPath);
            File.Delete(pifWithoutSdkIntPath);
            File.Delete(pifWithBlankSdkIntPath);
        }
    }

    private static void ConfigureRootAccess(IAdbCommandService adb)
    {
        adb.RunAdbAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new CommandResult(0, string.Empty, string.Empty)));
        int identityCallCount = 0;
        adb.RunAdbShellAsync(Arg.Any<string>(), "whoami", Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(new CommandResult(
                0,
                identityCallCount++ % 2 == 0 ? "root" : "shell",
                string.Empty)));
    }

    private static DeviceIntegrityService CreateService(IAdbCommandService adb)
    {
        IRandomService random = Substitute.For<IRandomService>();
        random.PickRandom(Arg.Any<IReadOnlyList<Integrity>>())
            .Returns(callInfo => callInfo.Arg<IReadOnlyList<Integrity>>()[0]);

        return new DeviceIntegrityService(
            adb,
            random,
            NullLogger<DeviceIntegrityService>.Instance,
            new ImmediateRootAccessService());
    }

    private static UpdateIntegrityDialogResult CreateIntegrityResult(
        string pifPath,
        bool fakeDroidGuardSdkEnabled)
    {
        return new UpdateIntegrityDialogResult(
            updateIntegrityFromServer: false,
            updateIntegrityEnabled: true,
            updateKeyboxEnabled: false,
            updateIntegrityFile: pifPath,
            updateKeyboxFile: string.Empty,
            fakeDroidGuardSdkEnabled);
    }

    private static async Task ApplyPreparedIntegrityAsync(
        DeviceIntegrityService service,
        UpdateIntegrityDialogResult result)
    {
        PreparedIntegrityData preparedData = await service.PrepareAsync(result, CancellationToken.None);
        await service.ApplyPreparedAsync("SERIAL", preparedData, CancellationToken.None);
    }

    private static async Task<string> WritePifFileAsync(string? sdkInt)
    {
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".json");
        var integrity = new Integrity
        {
            FINGERPRINT = "google/redfin/redfin:13/TQ3A/123456:user/release-keys",
            SECURITY_PATCH = "2025-01-05",
            SDK_INT = sdkInt
        };
        await File.WriteAllTextAsync(path, System.Text.Json.JsonSerializer.Serialize(integrity));
        return path;
    }

    private sealed class ImmediateRootAccessService : IAdbRootAccessService
    {
        public Task ExecuteAsRootAsync(
            string serial,
            Func<CancellationToken, Task> action,
            CancellationToken cancellationToken)
        {
            return action(cancellationToken);
        }

        public Task<T> ExecuteAsRootAsync<T>(
            string serial,
            Func<CancellationToken, Task<T>> action,
            CancellationToken cancellationToken)
        {
            return action(cancellationToken);
        }
    }
}
