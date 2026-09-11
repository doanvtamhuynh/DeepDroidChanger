using System.Runtime.ExceptionServices;
using System.Windows;

namespace DeepDroidChanger.Tests.Views.ViewMultipleDevices;

[TestClass]
public sealed class ViewMultipleDevicesResourcesSmokeTests
{
    [TestMethod]
    public void ViewMultipleDevicesResources_LoadsExpectedResourceTypes()
    {
        (Type MarginType, Type HeaderHeightType, double HeaderHeight) result =
            RunOnSta(() =>
            {
                ResourceDictionary resources = new()
                {
                    Source = new Uri(
                        "/DeepDroidChanger;component/Views/ViewMultipleDevices/ViewMultipleDevicesResources.xaml",
                        UriKind.Relative)
                };

                object margin = resources["ViewMultipleDevices.TileMargin"];
                object headerHeight = resources["ViewMultipleDevices.TileHeaderHeight"];
                return (margin.GetType(), headerHeight.GetType(), (double)headerHeight);
            });

        Assert.AreEqual(typeof(Thickness), result.MarginType);
        Assert.AreEqual(typeof(double), result.HeaderHeightType);
        Assert.AreEqual(64d, result.HeaderHeight);
    }

    private static T RunOnSta<T>(Func<T> operation)
    {
        T result = default!;
        Exception? failure = null;
        Thread thread = new(() =>
        {
            try
            {
                result = operation();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (failure is not null)
            ExceptionDispatchInfo.Capture(failure).Throw();

        return result;
    }
}
