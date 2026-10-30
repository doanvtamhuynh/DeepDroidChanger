using DeepDroidChanger.Models;
using DeepDroidChanger.Services;
using DeepDroidChanger.ViewDevices.Contracts;
using DeepDroidChanger.ViewDevices.Models;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using ScrcpyNet;

namespace DeepDroidChanger.Tests.Services.Implementations.ViewDevices;

[TestClass]
public sealed class ViewDeviceInteractionServiceTests
{
    [TestMethod]
    public async Task Clipboard_OnlyActiveSessionPublishesUnicode()
    {
        ClipboardFake clipboard = new();
        var service = Create(clipboard);
        var first = Session("A"); var second = Session("B");
        using var registrationA = service.Register(first, () => Task.CompletedTask, _ => { });
        using var registrationB = service.Register(second, () => Task.CompletedTask, _ => { });
        service.Activate(second);
        RaiseClipboard(first, "background");
        Assert.AreEqual(0, clipboard.Attempts);
        string unicode = "Tiếng Việt 😀 日本語 中文\tline\r\nnext";
        RaiseClipboard(second, unicode);
        await clipboard.Committed.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.AreEqual(unicode, clipboard.Text);
    }

    [TestMethod]
    public async Task Clipboard_FocusChangesDuringHostRead_DropsOldEventEvenAfterReturningToDevice()
    {
        ClipboardFake clipboard = new();
        TaskCompletionSource<string?> read = new(TaskCreationOptions.RunContinuationsAsynchronously);
        clipboard.Read = () => read.Task;
        var service = Create(clipboard);
        var first = Session("A"); var second = Session("B");
        using var registrationA = service.Register(first, () => Task.CompletedTask, _ => { });
        using var registrationB = service.Register(second, () => Task.CompletedTask, _ => { });
        service.Activate(first);
        RaiseClipboard(first, "stale");
        service.Activate(second);
        service.Activate(first);
        read.SetResult("host");
        await clipboard.CommitAttempt.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.AreEqual("host", clipboard.Text);
        Assert.IsFalse(clipboard.Committed.Task.IsCompleted);
    }

    [TestMethod]
    public async Task Clipboard_ReplacementSessionCannotPublishOldQueuedText()
    {
        ClipboardFake clipboard = new();
        TaskCompletionSource<string?> read = new(TaskCreationOptions.RunContinuationsAsynchronously);
        clipboard.Read = () => read.Task;
        var service = Create(clipboard);
        var old = Session("A"); var replacement = Session("A");
        using var oldRegistration = service.Register(old, () => Task.CompletedTask, _ => { });
        service.Activate(old);
        RaiseClipboard(old, "stale");
        using var currentRegistration = service.Register(replacement, () => Task.CompletedTask, _ => { });
        read.SetResult("host");
        await clipboard.CommitAttempt.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.AreEqual("host", clipboard.Text);
        Assert.IsFalse(clipboard.Committed.Task.IsCompleted);
        oldRegistration.Dispose(); // Must not remove the replacement.
        clipboard.Read = null;
        RaiseClipboard(replacement, "current");
        await clipboard.Committed.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.AreEqual("current", clipboard.Text);
    }

    [TestMethod]
    public async Task Clipboard_RechecksOwnershipInsideFinalWriteAfterDispatch()
    {
        ClipboardFake clipboard = new() { DelayCommit = true };
        var service = Create(clipboard);
        var first = Session("A"); var second = Session("B");
        using var a = service.Register(first, () => Task.CompletedTask, _ => { });
        using var b = service.Register(second, () => Task.CompletedTask, _ => { });
        service.Activate(first);
        RaiseClipboard(first, "stale");
        await clipboard.CommitEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        service.Activate(second);
        clipboard.AllowCommit.SetResult();
        await clipboard.CommitAttempt.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.AreEqual("host", clipboard.Text);
    }

    [TestMethod]
    public async Task Audio_StopsPreviousOwnerBeforeStartingNext_AndDefaultsOff()
    {
        var service = Create(new());
        Assert.IsTrue(service.PrepareOptions(new("A")).NoAudio);
        var a = Session("A"); var b = Session("B");
        List<string> changes = [];
        using var registrationA = service.Register(a, () =>
        { changes.Add("A:" + service.PrepareOptions(new("A")).NoAudio); return Task.CompletedTask; }, _ => { });
        using var registrationB = service.Register(b, () =>
        { changes.Add("B:" + service.PrepareOptions(new("B")).NoAudio); return Task.CompletedTask; }, _ => { });
        await service.ToggleAudioAsync(a, CancellationToken.None);
        await service.ToggleAudioAsync(b, CancellationToken.None);
        CollectionAssert.AreEqual(new[] { "A:False", "A:True", "B:False" }, changes);
        Assert.IsTrue(service.PrepareOptions(new("A")).NoAudio);
        Assert.IsFalse(service.PrepareOptions(new("B")).NoAudio);
        await service.ToggleAudioAsync(b, CancellationToken.None);
        Assert.IsTrue(service.PrepareOptions(new("B")).NoAudio);
    }

    [TestMethod]
    public async Task Audio_FailedPreviousStopDoesNotStartAnotherSource()
    {
        var service = Create(new());
        var a = Session("A"); var b = Session("B");
        int aCalls = 0, bCalls = 0;
        using var first = service.Register(a, () => ++aCalls == 1 ? Task.CompletedTask : Task.FromException(new IOException("stop failed")), _ => { });
        using var second = service.Register(b, () => { bCalls++; return Task.CompletedTask; }, _ => { });
        await service.ToggleAudioAsync(a, CancellationToken.None);
        await Assert.ThrowsExactlyAsync<IOException>(() => service.ToggleAudioAsync(b, CancellationToken.None));
        Assert.AreEqual(0, bCalls);
        Assert.IsFalse(service.PrepareOptions(new("A")).NoAudio);
        Assert.IsTrue(service.PrepareOptions(new("B")).NoAudio);
    }

    [TestMethod]
    public async Task Audio_UnavailableCaptureReconnectsWithVideoOnly()
    {
        var service = Create(new());
        var session = Session("A");
        int reconnects = 0;
        TaskCompletionSource reported = new(TaskCreationOptions.RunContinuationsAsynchronously);
        using var registration = service.Register(session, () => { reconnects++; return Task.CompletedTask; },
            key => { if (key == "ViewDevice_AudioUnavailable") reported.TrySetResult(); });
        await service.ToggleAudioAsync(session, CancellationToken.None);
        session.AudioState.Returns(ScrcpyAudioState.Unavailable);
        session.FeaturesChanged += Raise.Event<EventHandler>(session, EventArgs.Empty);
        await reported.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.IsTrue(service.PrepareOptions(new("A")).NoAudio);
        Assert.AreEqual(2, reconnects);
    }

    [TestMethod]
    public async Task Keyboard_PersistsPreferenceAndUsesSdkAfterUhidFallback()
    {
        var service = Create(new());
        var original = Session("A");
        using var registration = service.Register(original, () => Task.CompletedTask, _ => { });
        await service.SetKeyboardModeAsync(original, ViewDeviceKeyboardMode.Uhid, CancellationToken.None);
        Assert.AreEqual(ViewDeviceKeyboardMode.Uhid, service.PrepareOptions(new("A")).KeyboardMode);
        var fallback = Session("A"); fallback.UhidFallback.Returns(true);
        using var replacement = service.Register(fallback, () => Task.CompletedTask, _ => { });
        Assert.AreEqual(ViewDeviceKeyboardMode.Sdk, service.PrepareOptions(new("A")).KeyboardMode);
    }

    [TestMethod]
    public async Task Transfer_UsesUniqueBoundedUnicodeNameAndScansSuccessfulPush()
    {
        IAdbCommandService adb = Substitute.For<IAdbCommandService>();
        string? destination = null;
        adb.PushFileAsync("A", Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call => { destination = call.ArgAt<string>(2); return new CommandResult(0, "", ""); });
        var service = Create(new(), adb);
        var session = Session("A");
        using var registration = service.Register(session, () => Task.CompletedTask, _ => { });
        string directory = Path.Combine(Path.GetTempPath(), "scrcpy-transfer-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "Tiếng Việt 😀.txt");
        try
        {
            await File.WriteAllTextAsync(path, "content");
            await service.PushFileAsync(session, path, CancellationToken.None);
            string first = destination!;
            await session.Received().ScanFileAsync(first, CancellationToken.None);
            StringAssert.StartsWith(first, "/sdcard/Download/Tiếng Việt 😀-");
            StringAssert.EndsWith(first, ".txt");
            await service.PushFileAsync(session, path, CancellationToken.None);
            Assert.AreNotEqual(first, destination);
        }
        finally { File.Delete(path); Directory.Delete(directory); }
    }

    private static ViewDeviceInteractionService Create(ClipboardFake clipboard, IAdbCommandService? adb = null) =>
        new(clipboard, new ImmediateDispatcher(), adb ?? Substitute.For<IAdbCommandService>(), NullLogger<ViewDeviceInteractionService>.Instance);
    private static ISingleViewDeviceSession Session(string serial)
    {
        var session = Substitute.For<ISingleViewDeviceSession>();
        session.Serial.Returns(serial); session.State.Returns(SingleViewDeviceSessionState.Running);
        return session;
    }
    private static void RaiseClipboard(ISingleViewDeviceSession session, string text) =>
        session.ClipboardChanged += Raise.Event<EventHandler<ScrcpyClipboardChangedEventArgs>>(session, new ScrcpyClipboardChangedEventArgs(text));

    private sealed class ImmediateDispatcher : IUiDispatcherService
    {
        public bool CheckAccess() => true;
        public Task InvokeAsync(Action action, CancellationToken cancellationToken = default)
        { cancellationToken.ThrowIfCancellationRequested(); action(); return Task.CompletedTask; }
    }
    private sealed class ClipboardFake : IViewDeviceClipboardService
    {
        public string Text = "host";
        public int Attempts;
        public Func<Task<string?>>? Read;
        public bool DelayCommit;
        public TaskCompletionSource CommitEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource AllowCommit = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource CommitAttempt = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Committed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<string?> GetTextAsync(CancellationToken cancellationToken = default) => Read?.Invoke() ?? Task.FromResult<string?>(Text);
        public Task SetTextAsync(string text, CancellationToken cancellationToken = default) { Text = text; return Task.CompletedTask; }
        public async Task<bool> TrySetTextAsync(string text, Func<Action, bool> tryCommit, CancellationToken cancellationToken = default)
        {
            CommitEntered.TrySetResult();
            if (DelayCommit) await AllowCommit.Task;
            Attempts++;
            bool result = tryCommit(() => { Text = text; Committed.TrySetResult(); });
            CommitAttempt.TrySetResult();
            return result;
        }
    }
}
