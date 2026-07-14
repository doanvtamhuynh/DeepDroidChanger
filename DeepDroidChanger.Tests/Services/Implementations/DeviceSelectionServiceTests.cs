using DeepDroidChanger.Services;

namespace DeepDroidChanger.Tests.Services.Implementations
{
    [TestClass]
    public sealed class DeviceSelectionServiceTests
    {
        [TestMethod]
        public void FindSelection_RestoresHiddenMatchingSerialBeforeVisibleFallback()
        {
            var state = new DeviceSelectionService();
            var visible = new[] { "VISIBLE" };
            var all = new[] { "VISIBLE", "TARGET" };

            var selected = state.FindSelectionSerial("target", visible, all);

            Assert.AreEqual("TARGET", selected);
        }
    }
}
