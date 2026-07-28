using DeepDroidChanger.Models;
using DeepDroidChanger.Services;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace DeepDroidChanger.ViewModels
{
    public sealed partial class AddDevicesViewModel : ObservableObject, IDisposable
    {
        private const int DeviceRefreshSeconds = 3;

        private readonly IAdbDeviceService _adbDeviceService;
        private readonly IDeviceStoreService _deviceStoreService;
        private readonly ILogger<AddDevicesViewModel> _logger;
        private readonly IUiDispatcherService _uiDispatcher;
        private readonly ILocalizationService _localizationService;
        private readonly IPollingService _pollingService;
        private readonly CancellationTokenSource _refreshCancellation = new();
        private readonly SemaphoreSlim _refreshLock = new(1, 1);
        private bool _selectAll;
        private bool _isApplyingSelectAll;
        [ObservableProperty]
        private string _statusText = string.Empty;
        private bool _isDisposed;
        private Task? _refreshTask;

        public AddDevicesViewModel(
            IAdbDeviceService adbDeviceService,
            IDeviceStoreService deviceStoreService,
            IUiDispatcherService uiDispatcher,
            ILocalizationService localizationService,
            IPollingService pollingService,
            ILogger<AddDevicesViewModel> logger)
        {
            _adbDeviceService = adbDeviceService;
            _deviceStoreService = deviceStoreService;
            _uiDispatcher = uiDispatcher;
            _localizationService = localizationService;
            _pollingService = pollingService;
            _logger = logger;
            Devices = new ObservableCollection<AddDeviceRowViewModel>();
            TypeOptions = ["sargo", "starlte", "tissot", "unknown"];
        }

        public event EventHandler<bool>? CloseRequested;

        public ObservableCollection<AddDeviceRowViewModel> Devices { get; }
        public IReadOnlyList<string> TypeOptions { get; }
        public IReadOnlyList<StoredDeviceConfig> SelectedDevices { get; private set; } = Array.Empty<StoredDeviceConfig>();

        public async Task InitializeAsync(CancellationToken cancellationToken)
        {
            if (_refreshTask != null)
                return;

            await RefreshDevicesAsync(cancellationToken).ConfigureAwait(false);
            _refreshTask = _pollingService.RunAsync(
                TimeSpan.FromSeconds(DeviceRefreshSeconds),
                RefreshDevicesAsync,
                _refreshCancellation.Token);
        }

        public bool SelectAll
        {
            get => _selectAll;
            set
            {
                if (SetProperty(ref _selectAll, value))
                    ToggleSelectAll();
            }
        }

        public void Dispose()
        {
            if (_isDisposed)
                return;

            _isDisposed = true;
            _refreshCancellation.Cancel();
            if (_refreshTask is null or { IsCompleted: true })
            {
                ClearRows();
                _refreshCancellation.Dispose();
                return;
            }

            _ = _refreshTask.ContinueWith(
                async _ =>
                {
                    try
                    {
                        await _uiDispatcher.InvokeAsync(ClearRows).ConfigureAwait(false);
                    }
                    catch (Exception exception)
                    {
                        _logger.LogDebug(exception, "Failed to clear Add Devices rows during disposal.");
                    }
                    finally
                    {
                        _refreshCancellation.Dispose();
                    }
                },
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default).Unwrap();
        }

        public async Task DeactivateAsync()
        {
            _refreshCancellation.Cancel();
            if (_refreshTask != null)
                await _refreshTask.ConfigureAwait(false);

            _refreshTask = null;
            await _uiDispatcher.InvokeAsync(ClearRows).ConfigureAwait(false);
        }

        private async Task RefreshDevicesAsync(CancellationToken cancellationToken)
        {
            if (!await _refreshLock.WaitAsync(0, cancellationToken).ConfigureAwait(false))
                return;

            try
            {
                var rows = await LoadAddableRowsAsync(cancellationToken).ConfigureAwait(false);
                await RunOnUiContextAsync(() => SyncRows(rows)).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Failed to refresh addable devices.");
                await RunOnUiContextAsync(() =>
                    StatusText = _localizationService.GetString("AddDevices_LoadFailed")).ConfigureAwait(false);
            }
            finally
            {
                _refreshLock.Release();
            }
        }

        private async Task<IReadOnlyList<AddDeviceRowViewModel>> LoadAddableRowsAsync(CancellationToken cancellationToken)
        {
            var connectedDevices = await _adbDeviceService.GetConnectedDevicesAsync(cancellationToken).ConfigureAwait(false);
            var storedConfigs = await _deviceStoreService.LoadAsync(cancellationToken).ConfigureAwait(false);

            var storedSerials = new HashSet<string>(
                storedConfigs.Select(config => config.Serial),
                StringComparer.OrdinalIgnoreCase);

            var addableRows = new List<AddDeviceRowViewModel>();

            foreach (var device in connectedDevices)
            {
                if (device.Status != AdbDeviceStatus.Online || storedSerials.Contains(device.Serial))
                    continue;

                var type = await _adbDeviceService.GetDeviceTypeAsync(device.Serial, cancellationToken).ConfigureAwait(false);
                addableRows.Add(new AddDeviceRowViewModel(device.Serial, type));
            }

            return addableRows;
        }

        private void SyncRows(IReadOnlyList<AddDeviceRowViewModel> latestRows)
        {
            for (var index = Devices.Count - 1; index >= 0; index--)
            {
                var existingRow = Devices[index];
                if (!latestRows.Any(row => SerialEquals(row.Serial, existingRow.Serial)))
                {
                    existingRow.PropertyChanged -= OnDeviceRowPropertyChanged;
                    Devices.RemoveAt(index);
                }
            }

            foreach (var latestRow in latestRows)
            {
                if (Devices.Any(row => SerialEquals(row.Serial, latestRow.Serial)))
                    continue;

                latestRow.PropertyChanged += OnDeviceRowPropertyChanged;
                Devices.Add(latestRow);
            }

            UpdateSelectionState();
        }

        private void ToggleSelectAll()
        {
            var targetState = SelectAll;
            _isApplyingSelectAll = true;
            foreach (var device in Devices)
                device.IsSelected = targetState;

            _isApplyingSelectAll = false;
            AddCommand.NotifyCanExecuteChanged();
        }

        [RelayCommand(CanExecute = nameof(CanAdd))]
        private void Add()
        {
            SelectedDevices = Devices
                .Where(device => device.IsSelected)
                .Select(device => new StoredDeviceConfig
                {
                    Serial = device.Serial,
                    Type = device.Type,
                    Name = device.Name
                })
                .ToList();

            RequestClose(true);
        }

        private bool CanAdd()
        {
            return Devices.Any(device => device.IsSelected);
        }

        private void UpdateSelectionState()
        {
            if (_isApplyingSelectAll)
                return;

            var areAllSelected = Devices.Count > 0 && Devices.All(device => device.IsSelected);
            SetProperty(ref _selectAll, areAllSelected, nameof(SelectAll));
            AddCommand.NotifyCanExecuteChanged();
        }

        private void OnDeviceRowPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
        {
            if (args.PropertyName == nameof(AddDeviceRowViewModel.IsSelected))
                UpdateSelectionState();
        }

        private void ClearRows()
        {
            foreach (var device in Devices)
                device.PropertyChanged -= OnDeviceRowPropertyChanged;

            Devices.Clear();
        }

        private void RequestClose(bool result)
        {
            CloseRequested?.Invoke(this, result);
        }

        private Task RunOnUiContextAsync(Action action)
        {
            return _uiDispatcher.InvokeAsync(action);
        }

        private static bool SerialEquals(string left, string right)
        {
            return string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
        }
    }
}
