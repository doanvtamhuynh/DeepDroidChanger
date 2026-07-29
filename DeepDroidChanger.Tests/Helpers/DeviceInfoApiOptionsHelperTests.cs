using DeepDroidChanger.Helpers;
using DeepDroidChanger.Models;

namespace DeepDroidChanger.Tests.Helpers
{
    [TestClass]
    public sealed class DeviceInfoApiOptionsHelperTests
    {
        [TestMethod]
        public void Apply_ConfiguresValidInternalDefaultsWithoutAppsettingsFile()
        {
            DeviceInfoApiOptions options = new();
            DeviceInfoApiOptionsHelper.ApplyDefaults(options);

            Assert.IsTrue(Uri.TryCreate(options.Endpoint, UriKind.Absolute, out _));
            Assert.IsFalse(string.IsNullOrWhiteSpace(options.AuthorizationHeaderName));
            Assert.IsTrue(DeviceInfoApiOptionsHelper.IsValid(options));
        }
    }
}
