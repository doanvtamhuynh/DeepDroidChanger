using DeepDroidChanger.Models;
using DeepDroidChanger.Services;
using NSubstitute;

namespace DeepDroidChanger.Tests.Fakes;

internal static class DialogViewModelTestFactory
{
    public static IDeviceStoreService CreateStore(StoredDeviceConfig config)
    {
        IDeviceStoreService store = Substitute.For<IDeviceStoreService>();
        store.LoadAsync(Arg.Any<CancellationToken>()).Returns([config]);
        store.UpdateAsync(
                Arg.Any<string>(),
                Arg.Any<Action<StoredDeviceConfig>>(),
                Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                string serial = callInfo.ArgAt<string>(0);
                if (!string.Equals(serial, config.Serial, StringComparison.OrdinalIgnoreCase))
                    return false;

                callInfo.ArgAt<Action<StoredDeviceConfig>>(1)(config);
                return true;
            });
        return store;
    }

    public static ILocalizationService CreateLocalizationService()
    {
        ILocalizationService localization = Substitute.For<ILocalizationService>();
        localization.GetString(Arg.Any<string>()).Returns("{0} - {1}");
        return localization;
    }
}
