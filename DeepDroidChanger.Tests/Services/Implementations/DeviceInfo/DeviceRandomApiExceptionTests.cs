using DeepDroidChanger.Services;

namespace DeepDroidChanger.Tests.Services.Implementations.DeviceInfo
{
    [TestClass]
    public sealed class DeviceRandomApiExceptionTests
    {
        [TestMethod]
        public void Constructor_Message_StoresMessage()
        {
            var exception = new DeviceRandomApiException("api failed");

            Assert.AreEqual("api failed", exception.Message);
        }
    }
}
