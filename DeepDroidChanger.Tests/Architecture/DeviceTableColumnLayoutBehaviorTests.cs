using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using DeepDroidChanger.Behaviors;

namespace DeepDroidChanger.Tests.Architecture;

[TestClass]
[DoNotParallelize]
public sealed class DeviceTableColumnLayoutBehaviorTests
{
    [TestMethod]
    public void InitialOpen_AppliesPersistedRatiosWithoutSaving()
    {
        RunInSta(() =>
        {
            (DataGrid grid, DataGridColumn name, DataGridColumn process) = CreateGrid();
            var command = new RecordingCommand();
            DeviceTableColumnLayoutBehavior.SetSaveColumnRatiosCommand(grid, command);
            DeviceTableColumnLayoutBehavior.SetColumnRatios(
                grid,
                new Dictionary<string, double>
                {
                    ["Name"] = 0.25,
                    ["Process"] = 0.75
                });
            DeviceTableColumnLayoutBehavior.SetPersistColumnRatios(grid, true);

            grid.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent, grid));

            AssertStarWidth(name, 0.25);
            AssertStarWidth(process, 0.75);
            Assert.IsEmpty(command.Executions);

            grid.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent, grid));
            DeviceTableColumnLayoutBehavior.SetPersistColumnRatios(grid, false);
        });
    }

    [TestMethod]
    public void ResizeWhileOpen_SavesNormalizedRatiosAndRestoresResponsiveStarWidths()
    {
        RunInSta(() =>
        {
            (DataGrid grid, DataGridColumn name, DataGridColumn process) = CreateGrid();
            var command = new RecordingCommand(ratios =>
                DeviceTableColumnLayoutBehavior.SetColumnRatios(
                    grid,
                    new Dictionary<string, double>(ratios, StringComparer.Ordinal)));
            DeviceTableColumnLayoutBehavior.SetSaveColumnRatiosCommand(grid, command);
            DeviceTableColumnLayoutBehavior.SetPersistColumnRatios(grid, true);
            grid.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent, grid));

            name.Width = new DataGridLength(100, DataGridLengthUnitType.Pixel);
            process.Width = new DataGridLength(300, DataGridLengthUnitType.Pixel);
            MeasureGrid(grid);
            PumpDispatcher(TimeSpan.FromMilliseconds(350));

            Assert.HasCount(1, command.Executions);
            IReadOnlyDictionary<string, double> saved = command.Executions[0];
            Assert.AreEqual(0.25, saved["Name"], 0.01);
            Assert.AreEqual(0.75, saved["Process"], 0.01);
            AssertStarWidth(name, saved["Name"]);
            AssertStarWidth(process, saved["Process"]);

            grid.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent, grid));
            DeviceTableColumnLayoutBehavior.SetPersistColumnRatios(grid, false);
        });
    }

    [TestMethod]
    public void LeaveBeforeDebounce_FlushesLatestRatiosAndDefersReapplyUntilReopen()
    {
        RunInSta(() =>
        {
            (DataGrid grid, DataGridColumn name, DataGridColumn process) = CreateGrid();
            var command = new RecordingCommand();
            DeviceTableColumnLayoutBehavior.SetSaveColumnRatiosCommand(grid, command);
            DeviceTableColumnLayoutBehavior.SetPersistColumnRatios(grid, true);
            grid.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent, grid));

            name.Width = new DataGridLength(120, DataGridLengthUnitType.Pixel);
            process.Width = new DataGridLength(280, DataGridLengthUnitType.Pixel);
            MeasureGrid(grid);
            grid.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent, grid));

            Assert.HasCount(1, command.Executions);
            IReadOnlyDictionary<string, double> saved = command.Executions[0];
            Assert.AreEqual(0.3, saved["Name"], 0.01);
            Assert.AreEqual(0.7, saved["Process"], 0.01);

            DeviceTableColumnLayoutBehavior.SetColumnRatios(grid, saved);
            Assert.AreEqual(DataGridLengthUnitType.Pixel, name.Width.UnitType);
            Assert.AreEqual(DataGridLengthUnitType.Pixel, process.Width.UnitType);

            grid.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent, grid));
            AssertStarWidth(name, saved["Name"]);
            AssertStarWidth(process, saved["Process"]);
            Assert.HasCount(1, command.Executions);

            grid.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent, grid));
            DeviceTableColumnLayoutBehavior.SetPersistColumnRatios(grid, false);
        });
    }

    private static (DataGrid Grid, DataGridColumn Name, DataGridColumn Process) CreateGrid()
    {
        var grid = new DataGrid
        {
            Width = 400,
            Height = 120,
            CanUserResizeColumns = true
        };
        var name = new DataGridTextColumn
        {
            Width = new DataGridLength(1, DataGridLengthUnitType.Star)
        };
        var process = new DataGridTextColumn
        {
            Width = new DataGridLength(1, DataGridLengthUnitType.Star)
        };
        DeviceTableColumnLayoutBehavior.SetColumnKey(name, "Name");
        DeviceTableColumnLayoutBehavior.SetColumnKey(process, "Process");
        grid.Columns.Add(name);
        grid.Columns.Add(process);
        MeasureGrid(grid);
        return (grid, name, process);
    }

    private static void MeasureGrid(DataGrid grid)
    {
        var available = new Size(grid.Width, grid.Height);
        grid.Measure(available);
        grid.Arrange(new Rect(available));
        grid.UpdateLayout();
    }

    private static void AssertStarWidth(DataGridColumn column, double expected)
    {
        Assert.AreEqual(DataGridLengthUnitType.Star, column.Width.UnitType);
        Assert.AreEqual(expected, column.Width.Value, 0.000001);
    }

    private static void PumpDispatcher(TimeSpan duration)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(
            duration,
            DispatcherPriority.Background,
            (_, _) => frame.Continue = false,
            Dispatcher.CurrentDispatcher);
        timer.Start();
        Dispatcher.PushFrame(frame);
        timer.Stop();
    }

    private static void RunInSta(Action action)
    {
        Exception? failure = null;
        using var completed = new ManualResetEventSlim();
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
            finally
            {
                completed.Set();
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        Assert.IsTrue(completed.Wait(TimeSpan.FromSeconds(10)), "STA behavior test timed out.");
        thread.Join();
        if (failure != null)
            throw new AssertFailedException("STA behavior test failed.", failure);
    }

    private sealed class RecordingCommand(
        Action<IReadOnlyDictionary<string, double>>? onExecute = null) : ICommand
    {
        public List<IReadOnlyDictionary<string, double>> Executions { get; } = [];

        public bool CanExecute(object? parameter) => true;

        public void Execute(object? parameter)
        {
            var ratios = Assert.IsInstanceOfType<IReadOnlyDictionary<string, double>>(parameter);
            var snapshot = new Dictionary<string, double>(ratios, StringComparer.Ordinal);
            Executions.Add(snapshot);
            onExecute?.Invoke(snapshot);
        }

        public event EventHandler? CanExecuteChanged
        {
            add
            {
            }
            remove
            {
            }
        }
    }
}
