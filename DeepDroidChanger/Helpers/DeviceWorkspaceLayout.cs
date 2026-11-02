using System.Windows;
using System.Windows.Controls;

namespace DeepDroidChanger.Helpers;

// Shared presentation layout for the single- and multiple-device workspaces.
internal static class DeviceWorkspaceLayout
{
    public static void Arrange(Grid root, Grid profiles, FrameworkElement information,
        FrameworkElement configuration, FrameworkElement actions, Grid toolbar, FrameworkElement search)
    {
        double gap = ((Thickness)root.FindResource("Spacing.PageMargin")).Left;
        double scale = (double)root.FindResource("Metric.FontSize.Body") / 14;
        int columns = root.ActualWidth >= 1040 * scale ? 3 : root.ActualWidth >= 820 * scale ? 2 : 1;
        int rows = columns == 3 ? 1 : columns == 2 ? 2 : 3;
        if (profiles.RowDefinitions.Count != rows)
        {
            profiles.RowDefinitions.Clear();
            for (int row = 0; row < rows; row++)
                profiles.RowDefinitions.Add(new() { Height = GridLength.Auto });
        }

        root.RowDefinitions[2].MinHeight = Math.Min(180, Math.Max(80, root.ActualHeight * .2));
        root.RowDefinitions[4].MinHeight = Math.Min(220, Math.Max(96, root.ActualHeight * .25));
        if (root.FindName("WorkspaceDescription") is FrameworkElement description)
            description.Visibility = root.ActualHeight < 560 * scale ? Visibility.Collapsed : Visibility.Visible;
        ArrangeHeader(information, gap, scale);
        ArrangeHeader(configuration, gap, scale);

        profiles.ColumnDefinitions[0].Width = new(columns == 3 ? 2.2 : columns == 2 ? 1.5 : 1, GridUnitType.Star);
        profiles.ColumnDefinitions[1].Width = columns > 1 ? new(columns == 3 ? 1.35 : 1, GridUnitType.Star) : new(0);
        profiles.ColumnDefinitions[2].Width = columns == 3 ? new(1.45, GridUnitType.Star) : new(0);
        FrameworkElement[] panels = [information, configuration, actions];
        for (int index = 0; index < panels.Length; index++)
        {
            int row = columns == 3 ? 0 : columns == 2 ? index / 2 : index;
            int column = columns == 3 ? index : columns == 2 ? index % 2 : 0;
            Grid.SetRow(panels[index], row);
            Grid.SetColumn(panels[index], column);
            Grid.SetColumnSpan(panels[index], columns == 2 && index == 2 ? 2 : 1);
            panels[index].Margin = new(0, row > 0 ? gap : 0, column < columns - 1 && !(columns == 2 && index == 2) ? gap : 0, 0);
        }

        bool compactToolbar = root.ActualWidth < 920 * scale;
        if (toolbar.RowDefinitions.Count == 0)
        {
            toolbar.RowDefinitions.Add(new() { Height = GridLength.Auto });
            toolbar.RowDefinitions.Add(new() { Height = GridLength.Auto });
        }
        Grid.SetRow(search, compactToolbar ? 1 : 0);
        Grid.SetColumn(search, compactToolbar ? 0 : 2);
        Grid.SetColumnSpan(search, compactToolbar ? 4 : 1);
        search.MaxWidth = double.PositiveInfinity;
        search.Margin = compactToolbar ? new(0, gap, 0, 0) : new(gap, 0, gap, 0);

        bool narrow = root.ActualWidth < 620 * scale;
        for (int index = 0; index < toolbar.ColumnDefinitions.Count; index++)
            toolbar.ColumnDefinitions[index].Width = narrow || index == 2
                ? new(1, GridUnitType.Star) : GridLength.Auto;
        if (toolbar.Children.OfType<Button>().FirstOrDefault() is { } add)
            Grid.SetColumnSpan(add, narrow ? 2 : 1);
        if (toolbar.Children.OfType<TextBlock>().FirstOrDefault() is { } count)
        {
            Grid.SetColumn(count, narrow ? 2 : 1);
            Grid.SetColumnSpan(count, narrow ? 2 : 1);
        }
        if (toolbar.Children.OfType<StackPanel>().FirstOrDefault() is { } filter)
        {
            Grid.SetRow(filter, narrow ? 1 : 0);
            Grid.SetColumn(filter, narrow ? 2 : 3);
            Grid.SetColumnSpan(filter, narrow ? 2 : 1);
            filter.Margin = narrow ? new(gap / 2, gap, 0, 0) : new(0);
        }
        if (narrow)
        {
            Grid.SetColumnSpan(search, 2);
            search.Margin = new(0, gap, gap / 2, 0);
        }
    }

    private static void ArrangeHeader(FrameworkElement panel, double gap, double scale)
    {
        if (panel is not Border { Child: Grid body }) return;
        Grid? header = body.Children.OfType<Grid>().FirstOrDefault(grid => Grid.GetRow(grid) == 0);
        Button? button = header?.Children.OfType<Button>().FirstOrDefault();
        if (header is null || button is null) return;
        if (header.RowDefinitions.Count == 0)
        {
            header.RowDefinitions.Add(new() { Height = GridLength.Auto });
            header.RowDefinitions.Add(new() { Height = GridLength.Auto });
        }
        bool compact = panel.ActualWidth < 420 * scale;
        Grid.SetRow(button, compact ? 1 : 0);
        Grid.SetColumn(button, compact ? 0 : 2);
        Grid.SetColumnSpan(button, compact ? 3 : 1);
        button.HorizontalAlignment = compact ? HorizontalAlignment.Left : HorizontalAlignment.Right;
        button.Margin = compact ? new(0, gap / 2, 0, 0) : new(0);
    }
}
