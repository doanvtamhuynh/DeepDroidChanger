using System.ComponentModel;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using DeepDroidChanger.ViewModels;

namespace DeepDroidChanger.Views;

public sealed partial class ViewDeviceToolsWindow : Window
{
    private const double DetailWindowVisibleGap = 12;
    private const double DetailWindowShadowMargin = 8;

    private readonly ViewDeviceViewModel _viewModel;
    private ViewDeviceToolDetailWindow? _detailWindow;
    private bool _isClosing;

    public ViewDeviceToolsWindow(ViewDeviceViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);

        _viewModel = viewModel;
        InitializeComponent();
        DataContext = viewModel;

        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        LocationChanged += OnToolsWindowLocationChanged;
        SizeChanged += OnToolsWindowSizeChanged;
        StateChanged += OnToolsWindowStateChanged;
        PreviewMouseDown += OnPreviewMouseDown;
    }

    internal void DismissDetailWindow()
    {
        _viewModel.CloseToolEditor();
        if (_detailWindow?.IsVisible == true)
            _detailWindow.Hide();
    }

    internal void CloseDetailWindow()
    {
        _viewModel.CloseToolEditor();
        ViewDeviceToolDetailWindow? detailWindow = _detailWindow;
        _detailWindow = null;
        if (detailWindow is null)
            return;

        detailWindow.SizeChanged -= OnDetailWindowSizeChanged;
        detailWindow.DataContext = null;
        if (detailWindow.IsLoaded)
            detailWindow.Close();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs eventArgs)
    {
        if (_isClosing || eventArgs.PropertyName != nameof(ViewDeviceViewModel.ActiveToolEditor))
            return;

        if (_viewModel.ActiveToolEditor == ViewDeviceToolEditor.None)
        {
            if (_detailWindow?.IsVisible == true)
                _detailWindow.Hide();
            return;
        }

        ShowDetailWindow(_viewModel.ActiveToolEditor);
    }

    private void ShowDetailWindow(ViewDeviceToolEditor editor)
    {
        ViewDeviceToolDetailWindow detailWindow = EnsureDetailWindow();
        detailWindow.SetEditor(editor);
        UpdateDetailWindowPosition();
        if (!detailWindow.IsVisible)
            detailWindow.Show();
        else
            detailWindow.Activate();

        UpdateDetailWindowPosition();
        detailWindow.FocusPrimaryInput();
    }

    private ViewDeviceToolDetailWindow EnsureDetailWindow()
    {
        if (_detailWindow is not null)
            return _detailWindow;

        ViewDeviceToolDetailWindow detailWindow = new(_viewModel)
        {
            Owner = this,
            DataContext = _viewModel,
            ShowInTaskbar = false
        };
        detailWindow.SizeChanged += OnDetailWindowSizeChanged;
        _detailWindow = detailWindow;
        return detailWindow;
    }

    private void UpdateDetailWindowPosition()
    {
        if (_detailWindow is not { IsVisible: true } detailWindow ||
            Owner is not Window owner ||
            WindowState == WindowState.Minimized ||
            !IsVisible)
        {
            return;
        }

        Rect workArea = ViewDeviceMonitorWorkArea.GetFor(this);
        double ownerWidth = owner.ActualWidth > 0 ? owner.ActualWidth : owner.Width;
        double ownerHeight = owner.ActualHeight > 0 ? owner.ActualHeight : owner.Height;
        double toolsWidth = ActualWidth > 0 ? ActualWidth : Width;
        double toolsHeight = ActualHeight > 0 ? ActualHeight : Height;
        double detailWidth = detailWindow.ActualWidth > 0
            ? detailWindow.ActualWidth
            : double.IsFinite(detailWindow.Width) && detailWindow.Width > 0
                ? detailWindow.Width
                : 350;
        double detailHeight = detailWindow.ActualHeight > 0
            ? detailWindow.ActualHeight
            : double.IsFinite(detailWindow.Height) && detailWindow.Height > 0
                ? detailWindow.Height
                : 300;
        if (!double.IsFinite(ownerWidth) ||
            !double.IsFinite(ownerHeight) ||
            !double.IsFinite(toolsWidth) ||
            !double.IsFinite(toolsHeight))
        {
            return;
        }

        Rect ownerBounds = new(
            double.IsFinite(owner.Left) ? owner.Left : workArea.Left,
            double.IsFinite(owner.Top) ? owner.Top : workArea.Top,
            Math.Max(1, ownerWidth),
            Math.Max(1, ownerHeight));
        Rect toolsBounds = new(
            double.IsFinite(Left) ? Left : workArea.Left,
            double.IsFinite(Top) ? Top : workArea.Top,
            Math.Max(1, toolsWidth),
            Math.Max(1, toolsHeight));
        Point position = ViewDeviceToolDetailPlacement.Calculate(
            ownerBounds,
            toolsBounds,
            new Size(detailWidth, detailHeight),
            workArea,
            DetailWindowVisibleGap,
            DetailWindowShadowMargin);
        detailWindow.Left = position.X;
        detailWindow.Top = position.Y;
    }

    private void OnToolsWindowLocationChanged(object? sender, EventArgs eventArgs)
    {
        UpdateDetailWindowPosition();
    }

    private void OnToolsWindowSizeChanged(object? sender, SizeChangedEventArgs eventArgs)
    {
        UpdateDetailWindowPosition();
    }

    private void OnDetailWindowSizeChanged(object? sender, SizeChangedEventArgs eventArgs)
    {
        UpdateDetailWindowPosition();
    }

    private void OnToolsWindowStateChanged(object? sender, EventArgs eventArgs)
    {
        if (WindowState == WindowState.Minimized)
            DismissDetailWindow();
        else
            UpdateDetailWindowPosition();
    }

    private void OnPreviewMouseDown(object sender, MouseButtonEventArgs eventArgs)
    {
        DependencyObject? source = eventArgs.OriginalSource as DependencyObject;
        if (source is Thumb || FindAncestor<ScrollBar>(source) is not null)
            return;

        ButtonBase? button = FindButton(source);
        if (button is null || IsDetailAction(button))
            return;

        _viewModel.CloseToolEditor();
    }

    private static bool IsDetailAction(ButtonBase button) =>
        button.Name is "RenameConfigButton" or "InputAdbButton" or "FileTransferButton";

    private static ButtonBase? FindButton(DependencyObject? source)
    {
        DependencyObject? current = source;
        while (current is not null)
        {
            if (current is ButtonBase button)
                return button;

            current = current switch
            {
                Visual visual => VisualTreeHelper.GetParent(visual),
                FrameworkContentElement contentElement => contentElement.Parent,
                _ => null
            };
        }

        return null;
    }

    private static T? FindAncestor<T>(DependencyObject? source)
        where T : DependencyObject
    {
        DependencyObject? current = source;
        while (current is not null)
        {
            if (current is T match)
                return match;

            current = current switch
            {
                Visual visual => VisualTreeHelper.GetParent(visual),
                FrameworkContentElement contentElement => contentElement.Parent,
                _ => null
            };
        }

        return null;
    }

    protected override void OnClosed(EventArgs eventArgs)
    {
        _isClosing = true;
        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        LocationChanged -= OnToolsWindowLocationChanged;
        SizeChanged -= OnToolsWindowSizeChanged;
        StateChanged -= OnToolsWindowStateChanged;
        PreviewMouseDown -= OnPreviewMouseDown;
        CloseDetailWindow();
        base.OnClosed(eventArgs);
    }
}
