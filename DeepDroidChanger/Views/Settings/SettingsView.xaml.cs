using DeepDroidChanger.ViewModels;
using System.Windows;
using System.Windows.Controls;

namespace DeepDroidChanger.Views
{
    public sealed partial class SettingsView : UserControl
    {
        public SettingsView(SettingsViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;
            SettingsSections.SelectionChanged += (_, _) => SettingsScroll.ScrollToTop();
        }

        private void OnSizeChanged(object sender, SizeChangedEventArgs e)
        {
            bool compact = ActualWidth < 860;
            double gap = ((Thickness)FindResource("Spacing.PageMargin")).Left;
            AboutColumn.Width = compact ? new GridLength(0) : new GridLength(288);
            Grid.SetColumn(AboutPanel, compact ? 0 : 1);
            Grid.SetRow(AboutPanel, compact ? 1 : 0);
            AboutPanel.Margin = compact ? new Thickness(0, gap, 0, 0) : new Thickness(gap, 0, 0, 0);
            Grid.SetColumn(FooterActions, compact ? 0 : 1);
            Grid.SetRow(FooterActions, compact ? 1 : 0);
            Grid.SetColumnSpan(FooterActions, compact ? 2 : 1);
            Grid.SetColumnSpan(FooterStatus, compact ? 2 : 1);
            FooterActions.Margin = compact ? new Thickness(0, gap, 0, 0) : new Thickness(gap, 0, 0, 0);
            bool narrow = ActualWidth < 640;
            StylingFields.ColumnDefinitions[1].Width = narrow ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
            FrameworkElement[] fields = [StyleButtonColorField, StyleTextColorField, StyleFontFamilyField,
                StyleTypeStyleField, StyleFontSizeField, StyleButtonSizeField, StyleButtonVariantField];
            for (int index = 0; index < fields.Length; index++)
            {
                Grid.SetRow(fields[index], narrow ? index : index / 2);
                Grid.SetColumn(fields[index], narrow ? 0 : index % 2);
                Grid.SetColumnSpan(fields[index], !narrow && index == fields.Length - 1 ? 2 : 1);
                fields[index].Margin = new Thickness(0, 0, !narrow && index % 2 == 0 && index != fields.Length - 1 ? gap : 0, gap);
            }
        }

        private void OnShowStylingClick(object sender, RoutedEventArgs e)
        {
            var position = StylingPanel.TranslatePoint(new Point(0, 0), (UIElement)SettingsScroll.Content);
            SettingsScroll.ScrollToVerticalOffset(position.Y);
        }
    }
}


