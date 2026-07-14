using DeepDroidChanger.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace DeepDroidChanger.Tests.Services.Implementations;

[TestClass]
public sealed class TimezoneDataServiceTests
{
    [TestMethod]
    public async Task GetTimezonesAsync_BundledCatalog_ReturnsExpectedEntries()
    {
        TimezoneDataService service = new(NullLogger<TimezoneDataService>.Instance);

        IReadOnlyList<Models.TimezoneOption> timezones =
            await service.GetTimezonesAsync(CancellationToken.None);

        Assert.HasCount(418, timezones);
        Assert.IsTrue(timezones.Any(option => option.Timezone == "Asia/Ho_Chi_Minh"));
    }
}
