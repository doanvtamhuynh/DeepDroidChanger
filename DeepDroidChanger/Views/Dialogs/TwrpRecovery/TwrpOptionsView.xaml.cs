using System.Windows.Controls;
using System.Windows;
using System.Windows.Input;
namespace DeepDroidChanger.Views;
public partial class TwrpOptionsView : UserControl
{
    public TwrpOptionsView() => InitializeComponent();
    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;
        e.Handled = true;
        Window.GetWindow(this)?.Close();
    }
}
