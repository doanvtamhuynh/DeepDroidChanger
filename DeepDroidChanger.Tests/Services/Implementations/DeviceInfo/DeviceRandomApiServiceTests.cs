using System.Net;
using DeepDroidChanger.Models;
using DeepDroidChanger.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace DeepDroidChanger.Tests.Services.Implementations.DeviceInfo;

[TestClass]
public sealed class DeviceRandomApiServiceTests
{
    [TestMethod]
    public async Task GetRandomDeviceAsync_ApiFailure_ThrowsTypedExceptionWithoutRetrying()
    {
        var handler = new StubHttpMessageHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)));
        using var httpClient = new HttpClient(handler);
        using var service = new DeviceRandomApiService(
            httpClient,
            NullLogger<DeviceRandomApiService>.Instance);

        await Assert.ThrowsExactlyAsync<DeviceRandomApiException>(() => service.GetRandomDeviceAsync(
            CreateSession(),
            new RandomDeviceSelection("Google", 35),
            CancellationToken.None));

        Assert.AreEqual(1, handler.RequestCount);
    }

    [TestMethod]
    public async Task GetRandomDeviceAsync_Canceled_PropagatesCancellation()
    {
        var handler = new StubHttpMessageHandler(async (_, cancellationToken) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        using var httpClient = new HttpClient(handler);
        using var service = new DeviceRandomApiService(
            httpClient,
            NullLogger<DeviceRandomApiService>.Instance);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        await Assert.ThrowsExactlyAsync<TaskCanceledException>(() => service.GetRandomDeviceAsync(
            CreateSession(),
            new RandomDeviceSelection("Google", 35),
            cancellation.Token));

        Assert.AreEqual(1, handler.RequestCount);
    }

    [TestMethod]
    public async Task GetRandomDeviceAsync_HttpTimeout_ThrowsTypedFailureInsteadOfCancellation()
    {
        var handler = new StubHttpMessageHandler(async (_, cancellationToken) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        using var httpClient = new HttpClient(handler) { Timeout = TimeSpan.FromMilliseconds(100) };
        using var service = new DeviceRandomApiService(
            httpClient,
            NullLogger<DeviceRandomApiService>.Instance);

        await Assert.ThrowsExactlyAsync<DeviceRandomApiException>(() => service.GetRandomDeviceAsync(
            CreateSession(),
            new RandomDeviceSelection("Google", 35),
            CancellationToken.None));

        Assert.AreEqual(1, handler.RequestCount);
    }

    [TestMethod]
    public async Task GetRandomDeviceAsync_NetworkFailure_ThrowsTypedFailure()
    {
        var handler = new StubHttpMessageHandler((_, _) => throw new HttpRequestException("network unavailable"));
        using var httpClient = new HttpClient(handler);
        using var service = new DeviceRandomApiService(
            httpClient,
            NullLogger<DeviceRandomApiService>.Instance);

        await Assert.ThrowsExactlyAsync<DeviceRandomApiException>(() => service.GetRandomDeviceAsync(
            CreateSession(),
            new RandomDeviceSelection("Google", 35),
            CancellationToken.None));
    }

    [TestMethod]
    public async Task GetRandomDeviceAsync_ValidResponse_ReturnsDeviceAndSendsTokenHeader()
    {
        const string json = "{\"data\":{\"GetDeviceV4\":{\"model\":\"Pixel 8\",\"manufacturer\":\"Google\",\"imei\":\"123456789012345\"}}}";
        HttpRequestMessage? capturedRequest = null;
        var handler = new StubHttpMessageHandler((request, _) =>
        {
            capturedRequest = request;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json)
            });
        });
        using var httpClient = new HttpClient(handler);
        using var service = new DeviceRandomApiService(
            httpClient,
            NullLogger<DeviceRandomApiService>.Instance);

        DeviceInfoApiDevice device = await service.GetRandomDeviceAsync(
            CreateSession(),
            new RandomDeviceSelection("Google", 35),
            CancellationToken.None);

        Assert.AreEqual("Pixel 8", device.Model);
        Assert.IsNotNull(capturedRequest);
        Assert.AreEqual("test-token", capturedRequest.Headers.GetValues("X-Test-Auth").Single());
    }

    private static AccountSession CreateSession()
    {
        return new AccountSession("https://example.test/graphql", "X-Test-Auth", "test-token");
    }

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _handler;

        public StubHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler)
        {
            _handler = handler;
        }

        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestCount++;
            return _handler(request, cancellationToken);
        }
    }
}
