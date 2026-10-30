using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace DeepDroidChanger.ViewModels;

public sealed partial class ViewMultipleDevicesViewModel
{
    // The insertion stack and editable order live only for the current activation.
    private readonly List<ViewMultipleDeviceItemViewModel> _deviceStack = [];
    private readonly List<ViewMultipleDeviceItemViewModel> _deviceOrder = [];
    private int _devicesPerPage = PageSize;
    private int _sortIndex;
    private bool _isOrderEditorOpen;
    private bool _layoutRefreshRequested;

    public ObservableCollection<ViewMultipleDeviceItemViewModel> OrderedDevices { get; } = [];
    public IReadOnlyList<int> PageSizes { get; } = new[] { 4, 8, 12, 16, 24 };
    public int SelectedDeviceCount => _deviceStack.Count(item => item.IsSelected);
    public bool HasSelectedDevices => SelectedDeviceCount > 0;

    public int DevicesPerPage
    {
        get => _devicesPerPage;
        set
        {
            if (!IsBatchBusy && PageSizes.Contains(value) && SetProperty(ref _devicesPerPage, value))
            {
                RefreshDeviceOrder();
                ApplyLayout(1);
            }
        }
    }

    public int SortIndex
    {
        get => _sortIndex;
        set
        {
            if (!IsBatchBusy && value is 0 or 1 && SetProperty(ref _sortIndex, value))
            {
                RefreshDeviceOrder();
                if (value == 1)
                    IsOrderEditorOpen = true;
                ApplyLayout(1);
            }
        }
    }

    public bool IsOrderEditorOpen
    {
        get => _isOrderEditorOpen;
        set => SetProperty(ref _isOrderEditorOpen, value);
    }

    public IRelayCommand SelectPageCommand { get; private set; } = null!;
    public IRelayCommand SelectAllCommand { get; private set; } = null!;
    public IRelayCommand ClearSelectionCommand { get; private set; } = null!;
    public IRelayCommand EditOrderCommand { get; private set; } = null!;
    public IRelayCommand ResetOrderCommand { get; private set; } = null!;

    private void InitializePresentation()
    {
        SelectPageCommand = new RelayCommand(
            () => SetDeviceSelection(VisibleDevices, true), () => VisibleDevices.Count > 0);
        SelectAllCommand = new RelayCommand(
            () => SetDeviceSelection(_deviceStack, true), () => HasOnlineDevices);
        ClearSelectionCommand = new RelayCommand(
            () => SetDeviceSelection(_deviceStack, false), () => HasSelectedDevices);
        EditOrderCommand = new RelayCommand(() =>
        {
            bool showEditor = SortIndex != 0 || !IsOrderEditorOpen;
            SortIndex = 0;
            IsOrderEditorOpen = showEditor;
        }, () => !IsBatchBusy);
        ResetOrderCommand = new RelayCommand(() =>
        {
            _deviceOrder.Clear();
            _deviceOrder.AddRange(_deviceStack);
            SortIndex = 0;
            RefreshDeviceOrder();
            ApplyLayout(CurrentPage);
        }, () => HasOnlineDevices && !IsBatchBusy);
    }

    private void SynchronizeDeviceStack()
    {
        Dictionary<string, ViewMultipleDeviceItemViewModel> online = OnlineDevices.ToDictionary(
            item => item.Serial, StringComparer.OrdinalIgnoreCase);
        foreach (ViewMultipleDeviceItemViewModel item in _deviceStack.ToArray())
        {
            if (online.TryGetValue(item.Serial, out ViewMultipleDeviceItemViewModel? current) &&
                ReferenceEquals(current, item))
                continue;

            item.PropertyChanged -= OnDeviceItemChanged;
            item.IsInVisiblePage = false;
            item.IsSelected = false;
            _deviceStack.Remove(item);
            _deviceOrder.Remove(item);
        }

        HashSet<string> known = _deviceStack.Select(item => item.Serial)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (ViewMultipleDeviceItemViewModel item in OnlineDevices)
        {
            if (!known.Add(item.Serial))
                continue;

            _deviceStack.Add(item);
            _deviceOrder.Add(item);
            item.PropertyChanged += OnDeviceItemChanged;
        }

        RefreshDeviceOrder();
        foreach (ViewMultipleDeviceItemViewModel item in _deviceStack)
            item.IsInVisiblePage = VisibleDevices.Contains(item);
        NotifySelectionSummary();
        SelectPageCommand.NotifyCanExecuteChanged();
        SelectAllCommand.NotifyCanExecuteChanged();
        ResetOrderCommand.NotifyCanExecuteChanged();
    }

    private void OnDeviceItemChanged(object? sender, PropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.PropertyName == nameof(ViewMultipleDeviceItemViewModel.IsSelected))
        {
            NotifySelectionSummary();
            QueueTrackerRefresh();
        }
        else if (eventArgs.PropertyName == nameof(ViewMultipleDeviceItemViewModel.DeviceType))
            RefreshDeviceOrder();
        else if (eventArgs.PropertyName == nameof(ViewMultipleDeviceItemViewModel.IsRunning))
            OnPropertyChanged(nameof(BackgroundSessionCount));
    }

    private static void SetDeviceSelection(
        IEnumerable<ViewMultipleDeviceItemViewModel> items, bool selected)
    {
        foreach (ViewMultipleDeviceItemViewModel item in items)
            item.IsSelected = selected;
    }

    private void NotifySelectionSummary()
    {
        OnPropertyChanged(nameof(SelectedDeviceCount));
        OnPropertyChanged(nameof(HasSelectedDevices));
        ClearSelectionCommand.NotifyCanExecuteChanged();
        NotifyToolCommands();
    }

    private void RefreshDeviceOrder()
    {
        for (int index = 0; index < _deviceOrder.Count; index++)
            _deviceOrder[index].Index = index + 1;

        ViewMultipleDeviceItemViewModel[] displayOrder = _sortIndex == 1
            ? _deviceOrder.OrderBy(item => item.DeviceType, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.Index).ToArray()
            : _deviceOrder.ToArray();
        for (int index = 0; index < displayOrder.Length; index++)
        {
            ViewMultipleDeviceItemViewModel item = displayOrder[index];
            item.PageNumber = index / DevicesPerPage + 1;
            item.IsPageStart = index % DevicesPerPage == 0;
            item.PageStart = (item.PageNumber - 1) * DevicesPerPage + 1;
            item.PageEnd = Math.Min(item.PageNumber * DevicesPerPage, displayOrder.Length);
        }

        ApplyCollectionOrder(OrderedDevices, displayOrder);
    }

    internal void MoveOrderedDevice(string serial, string targetSerial, bool insertAfter)
    {
        if (SortIndex != 0 || IsBatchBusy)
            return;

        ViewMultipleDeviceItemViewModel? source = _deviceOrder.FirstOrDefault(item =>
            string.Equals(item.Serial, serial, StringComparison.OrdinalIgnoreCase));
        ViewMultipleDeviceItemViewModel? target = _deviceOrder.FirstOrDefault(item =>
            string.Equals(item.Serial, targetSerial, StringComparison.OrdinalIgnoreCase));
        if (source is null || target is null || ReferenceEquals(source, target))
            return;

        _deviceOrder.Remove(source);
        _deviceOrder.Insert(_deviceOrder.IndexOf(target) + (insertAfter ? 1 : 0), source);
        RefreshDeviceOrder();
        ApplyLayout(CurrentPage);
    }

    internal void MoveOrderedDeviceBy(string serial, int offset)
    {
        int index = _deviceOrder.FindIndex(item =>
            string.Equals(item.Serial, serial, StringComparison.OrdinalIgnoreCase));
        int target = index + offset;
        if (index >= 0 && target >= 0 && target < _deviceOrder.Count)
            MoveOrderedDevice(serial, _deviceOrder[target].Serial, offset > 0);
    }

    private void ApplyLayout(int page)
    {
        if (!Volatile.Read(ref _isActive)) return;
        _layoutRefreshRequested = true;
        RequestAbsolutePage(page);
        OnPropertyChanged(nameof(TotalPages));
        OnPropertyChanged(nameof(ShowingStart));
        OnPropertyChanged(nameof(ShowingEnd));
    }

    private async Task<ViewMultipleDeviceItemViewModel[]> SnapshotOrderedItemsAsync()
    {
        ViewMultipleDeviceItemViewModel[] items = [];
        await _uiDispatcher.InvokeAsync(() => items = OrderedDevices.ToArray()).ConfigureAwait(false);
        return items;
    }
}
