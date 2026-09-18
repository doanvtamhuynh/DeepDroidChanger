using System.Windows;
using System.Windows.Threading;

namespace DeepDroidChanger.Tests;

internal static class WpfTestDispatcher
{
    private static readonly object Gate = new();
    private static TaskCompletionSource<Dispatcher>? ready;
    private static Application? application;

    public static async Task RunAsync(Func<Task> operation)
    {
        ArgumentNullException.ThrowIfNull(operation);

        Dispatcher dispatcher = await GetDispatcherAsync().ConfigureAwait(false);
        await dispatcher.InvokeAsync(operation, DispatcherPriority.Normal)
            .Task
            .Unwrap()
            .ConfigureAwait(false);
    }

    public static void Run(Action operation)
    {
        RunAsync(() =>
        {
            operation();
            return Task.CompletedTask;
        }).GetAwaiter().GetResult();
    }

    public static T Run<T>(Func<T> operation)
    {
        T result = default!;
        Run((Action)(() => result = operation()));
        return result;
    }

    private static Task<Dispatcher> GetDispatcherAsync()
    {
        lock (Gate)
        {
            if (ready is not null)
                return ready.Task;

            ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
            Thread thread = new(() =>
            {
                try
                {
                    Dispatcher dispatcher = Dispatcher.CurrentDispatcher;
                    if (Application.Current is null)
                    {
                        application = new Application
                        {
                            ShutdownMode = ShutdownMode.OnExplicitShutdown
                        };
                        LoadApplicationResources(application);
                    }
                    else
                    {
                        application = Application.Current;
                        EnsureApplicationResources();
                    }

                    EnsureApplicationResources();
                    Application.Current!.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                    ready.TrySetResult(dispatcher);
                    Dispatcher.Run();
                }
                catch (Exception exception)
                {
                    ready.TrySetException(exception);
                }
            })
            {
                IsBackground = true,
                Name = "DeepDroidChanger shared WPF test dispatcher"
            };
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            return ready.Task;
        }
    }

    internal static void EnsureApplicationResources()
    {
        Application? application = Application.Current;
        if (application is null)
        {
            WpfTestDispatcher.application = new Application
            {
                ShutdownMode = ShutdownMode.OnExplicitShutdown
            };
            LoadApplicationResources(WpfTestDispatcher.application);
            application = Application.Current;
        }
        if (application is null)
            throw new InvalidOperationException("The WPF test dispatcher has no application.");

        if (application.Resources.MergedDictionaries.Count != 0)
        {
            return;
        }

        LoadApplicationResources(application);
    }

    private static void LoadApplicationResources(Application target)
    {
        target.Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri(
                "/DeepDroidChanger;component/Resources/Themes/Theme.Light.xaml",
                UriKind.Relative)
        });
        target.Resources.MergedDictionaries.Add(new MaterialDesignThemes.Wpf.BundledTheme
        {
            BaseTheme = MaterialDesignThemes.Wpf.BaseTheme.Light,
            PrimaryColor = MaterialDesignColors.PrimaryColor.Blue,
            SecondaryColor = MaterialDesignColors.SecondaryColor.LightBlue
        });
        target.Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri(
                "pack://application:,,,/MaterialDesignThemes.Wpf;component/Themes/MaterialDesign2.Defaults.xaml",
                UriKind.Absolute)
        });
        target.Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri(
                "/DeepDroidChanger;component/Resources/Themes/Controls.xaml",
                UriKind.Relative)
        });
        target.Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri(
                "/DeepDroidChanger;component/Resources/Strings/Strings.xaml",
                UriKind.Relative)
        });
    }

}
