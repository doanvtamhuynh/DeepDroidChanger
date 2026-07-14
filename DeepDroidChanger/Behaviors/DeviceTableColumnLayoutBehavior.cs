using DeepDroidChanger.Constants;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace DeepDroidChanger.Behaviors
{
    public static class DeviceTableColumnLayoutBehavior
    {
        private const int SaveDelayMilliseconds = 250;
        private const double MinimumTotalWidth = 1;

        public static readonly DependencyProperty PersistColumnRatiosProperty =
            DependencyProperty.RegisterAttached(
                "PersistColumnRatios",
                typeof(bool),
                typeof(DeviceTableColumnLayoutBehavior),
                new PropertyMetadata(false, OnPersistColumnRatiosChanged));

        public static readonly DependencyProperty ColumnKeyProperty =
            DependencyProperty.RegisterAttached(
                "ColumnKey",
                typeof(string),
                typeof(DeviceTableColumnLayoutBehavior),
                new PropertyMetadata(string.Empty));

        public static readonly DependencyProperty ColumnRatiosProperty =
            DependencyProperty.RegisterAttached(
                "ColumnRatios",
                typeof(IReadOnlyDictionary<string, double>),
                typeof(DeviceTableColumnLayoutBehavior),
                new PropertyMetadata(null));

        public static readonly DependencyProperty SaveColumnRatiosCommandProperty =
            DependencyProperty.RegisterAttached(
                "SaveColumnRatiosCommand",
                typeof(ICommand),
                typeof(DeviceTableColumnLayoutBehavior),
                new PropertyMetadata(null));

        private static readonly DependencyProperty StateProperty =
            DependencyProperty.RegisterAttached(
                "State",
                typeof(DeviceTableColumnLayoutState),
                typeof(DeviceTableColumnLayoutBehavior),
                new PropertyMetadata(null));

        public static bool GetPersistColumnRatios(DataGrid dataGrid)
        {
            return (bool)dataGrid.GetValue(PersistColumnRatiosProperty);
        }

        public static void SetPersistColumnRatios(DataGrid dataGrid, bool value)
        {
            dataGrid.SetValue(PersistColumnRatiosProperty, value);
        }

        public static string GetColumnKey(DataGridColumn column)
        {
            return (string)column.GetValue(ColumnKeyProperty);
        }

        public static void SetColumnKey(DataGridColumn column, string value)
        {
            column.SetValue(ColumnKeyProperty, value);
        }

        public static IReadOnlyDictionary<string, double>? GetColumnRatios(DataGrid dataGrid)
        {
            return (IReadOnlyDictionary<string, double>?)dataGrid.GetValue(ColumnRatiosProperty);
        }

        public static void SetColumnRatios(DataGrid dataGrid, IReadOnlyDictionary<string, double>? value)
        {
            dataGrid.SetValue(ColumnRatiosProperty, value);
        }

        public static ICommand? GetSaveColumnRatiosCommand(DataGrid dataGrid)
        {
            return (ICommand?)dataGrid.GetValue(SaveColumnRatiosCommandProperty);
        }

        public static void SetSaveColumnRatiosCommand(DataGrid dataGrid, ICommand? value)
        {
            dataGrid.SetValue(SaveColumnRatiosCommandProperty, value);
        }

        private static DeviceTableColumnLayoutState? GetState(DataGrid dataGrid)
        {
            return (DeviceTableColumnLayoutState?)dataGrid.GetValue(StateProperty);
        }

        private static void SetState(DataGrid dataGrid, DeviceTableColumnLayoutState? state)
        {
            dataGrid.SetValue(StateProperty, state);
        }

        private static void OnPersistColumnRatiosChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e)
        {
            if (dependencyObject is not DataGrid dataGrid)
                return;

            GetState(dataGrid)?.Detach();
            SetState(dataGrid, null);

            if (e.NewValue is true)
            {
                var state = new DeviceTableColumnLayoutState(dataGrid);
                SetState(dataGrid, state);
                state.Attach();
            }
        }

        private sealed class DeviceTableColumnLayoutState
        {
            private readonly DataGrid _dataGrid;
            private readonly DispatcherTimer _saveTimer;
            private readonly List<DataGridColumn> _subscribedColumns = new();
            private readonly EventHandler _columnWidthChangedHandler;
            private bool _isApplyingSavedRatios;

            public DeviceTableColumnLayoutState(DataGrid dataGrid)
            {
                _dataGrid = dataGrid;
                _columnWidthChangedHandler = OnColumnWidthChanged;
                _saveTimer = new DispatcherTimer
                {
                    Interval = TimeSpan.FromMilliseconds(SaveDelayMilliseconds)
                };
                _saveTimer.Tick += OnSaveTimerTick;
            }

            public void Attach()
            {
                _dataGrid.Loaded += OnLoaded;
                _dataGrid.Unloaded += OnUnloaded;

                if (_dataGrid.IsLoaded)
                {
                    OnLoaded(_dataGrid, new RoutedEventArgs());
                }
            }

            public void Detach()
            {
                _dataGrid.Loaded -= OnLoaded;
                _dataGrid.Unloaded -= OnUnloaded;
                SavePendingRatios();
                UnsubscribeColumns();
            }

            private void OnLoaded(object sender, RoutedEventArgs e)
            {
                ApplySavedRatios();
                SubscribeColumns();
            }

            private void OnUnloaded(object sender, RoutedEventArgs e)
            {
                SavePendingRatios();
                UnsubscribeColumns();
            }

            private void ApplySavedRatios()
            {
                _isApplyingSavedRatios = true;

                try
                {
                    ApplyRatios(GetColumnRatios(_dataGrid) ?? DeviceTableColumnSettings.DefaultRatios);
                }
                finally
                {
                    _isApplyingSavedRatios = false;
                }
            }

            private void SubscribeColumns()
            {
                UnsubscribeColumns();

                var descriptor = DependencyPropertyDescriptor.FromProperty(DataGridColumn.WidthProperty, typeof(DataGridColumn));
                foreach (var column in _dataGrid.Columns)
                {
                    if (string.IsNullOrWhiteSpace(GetColumnKey(column)))
                        continue;

                    descriptor.AddValueChanged(column, _columnWidthChangedHandler);
                    _subscribedColumns.Add(column);
                }
            }

            private void UnsubscribeColumns()
            {
                var descriptor = DependencyPropertyDescriptor.FromProperty(DataGridColumn.WidthProperty, typeof(DataGridColumn));
                foreach (var column in _subscribedColumns)
                {
                    descriptor.RemoveValueChanged(column, _columnWidthChangedHandler);
                }

                _subscribedColumns.Clear();
            }

            private void OnColumnWidthChanged(object? sender, EventArgs e)
            {
                if (_isApplyingSavedRatios || !_dataGrid.IsLoaded)
                    return;

                _saveTimer.Stop();
                _saveTimer.Start();
            }

            private void OnSaveTimerTick(object? sender, EventArgs e)
            {
                _saveTimer.Stop();
                SaveRatios();
            }

            private void SavePendingRatios()
            {
                if (!_saveTimer.IsEnabled)
                    return;

                _saveTimer.Stop();
                SaveRatios();
            }

            private void SaveRatios()
            {
                var ratios = GetCurrentRatios();
                if (ratios.Count == 0)
                    return;

                ICommand? command = GetSaveColumnRatiosCommand(_dataGrid);
                if (command?.CanExecute(ratios) == true)
                    command.Execute(ratios);
            }

            private void ApplyRatios(IReadOnlyDictionary<string, double> ratios)
            {
                foreach (var column in GetKeyedColumns())
                {
                    if (!ratios.TryGetValue(column.Key, out var ratio) || ratio <= 0)
                        continue;

                    column.Column.Width = new DataGridLength(ratio, DataGridLengthUnitType.Star);
                }
            }

            private Dictionary<string, double> GetCurrentRatios()
            {
                var keyedColumns = GetKeyedColumns()
                    .Where(column => column.Width > 0)
                    .ToList();

                var totalWidth = keyedColumns.Sum(column => column.Width);
                if (totalWidth < MinimumTotalWidth)
                    return new Dictionary<string, double>(DeviceTableColumnSettings.DefaultRatios);

                return keyedColumns.ToDictionary(
                    column => column.Key,
                    column => column.Width / totalWidth);
            }

            private List<KeyedColumn> GetKeyedColumns()
            {
                return _dataGrid.Columns
                    .Select(column => new KeyedColumn(column, GetColumnKey(column), column.ActualWidth))
                    .Where(column => !string.IsNullOrWhiteSpace(column.Key))
                    .ToList();
            }
        }

        private sealed record KeyedColumn(DataGridColumn Column, string Key, double Width);
    }
}
