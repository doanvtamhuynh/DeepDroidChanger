using System.Windows;
using System.Windows.Controls;

namespace DeepDroidChanger.Behaviors
{
    public static class ContextMenuOpenBehavior
    {
        public static readonly DependencyProperty IsOpenProperty =
            DependencyProperty.RegisterAttached(
                "IsOpen",
                typeof(bool),
                typeof(ContextMenuOpenBehavior),
                new FrameworkPropertyMetadata(
                    false,
                    FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
                    OnIsOpenChanged));

        public static bool GetIsOpen(DependencyObject element)
        {
            return (bool)element.GetValue(IsOpenProperty);
        }

        public static void SetIsOpen(DependencyObject element, bool value)
        {
            element.SetValue(IsOpenProperty, value);
        }

        private static void OnIsOpenChanged(
            DependencyObject dependencyObject,
            DependencyPropertyChangedEventArgs eventArgs)
        {
            if (dependencyObject is not FrameworkElement element ||
                element.ContextMenu is not { } contextMenu)
            {
                return;
            }

            contextMenu.Closed -= OnContextMenuClosed;

            if ((bool)eventArgs.NewValue)
            {
                contextMenu.PlacementTarget = element;
                contextMenu.Closed += OnContextMenuClosed;
                contextMenu.IsOpen = true;
                return;
            }

            contextMenu.IsOpen = false;
        }

        private static void OnContextMenuClosed(object sender, RoutedEventArgs eventArgs)
        {
            if (sender is not ContextMenu { PlacementTarget: DependencyObject placementTarget } contextMenu)
                return;

            contextMenu.Closed -= OnContextMenuClosed;
            placementTarget.SetCurrentValue(IsOpenProperty, false);
        }
    }
}
