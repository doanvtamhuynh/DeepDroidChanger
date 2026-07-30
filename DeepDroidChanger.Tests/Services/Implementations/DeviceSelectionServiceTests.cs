using DeepDroidChanger.Services;

namespace DeepDroidChanger.Tests.Services.Implementations
{
    [TestClass]
    public sealed class DeviceSelectionServiceTests
    {
        [TestMethod]
        public void FindSelection_RestoresHiddenMatchingSerial()
        {
            var state = new DeviceSelectionService();
            var visible = new[] { "VISIBLE" };
            var all = new[] { "VISIBLE", "TARGET" };

            var selected = state.FindSelectionSerial("target", visible, all);

            Assert.AreEqual("TARGET", selected);
        }

        [TestMethod]
        public void FindSelection_WithoutTarget_DoesNotSelectFirstVisibleDevice()
        {
            var state = new DeviceSelectionService();

            string? selected = state.FindSelectionSerial(
                string.Empty,
                ["FIRST", "SECOND"],
                ["FIRST", "SECOND"]);

            Assert.IsNull(selected);
        }

        [TestMethod]
        public void FindSelection_MissingTarget_DoesNotSelectFirstVisibleDevice()
        {
            var state = new DeviceSelectionService();

            string? selected = state.FindSelectionSerial(
                "MISSING",
                ["FIRST", "SECOND"],
                ["FIRST", "SECOND"]);

            Assert.IsNull(selected);
        }
    }
}
