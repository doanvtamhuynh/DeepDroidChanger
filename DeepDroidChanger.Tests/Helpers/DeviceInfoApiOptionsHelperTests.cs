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
            Assert.IsFalse(string.IsNullOrWhiteSpace(options.UserPoolId));
            Assert.IsFalse(string.IsNullOrWhiteSpace(options.ClientId));
            Assert.IsFalse(string.IsNullOrWhiteSpace(options.Region));
            Assert.IsFalse(string.IsNullOrWhiteSpace(options.AuthenticationHeaderName));
            Assert.IsTrue(DeviceInfoApiOptionsHelper.IsValid(options));
        }
    }
}
