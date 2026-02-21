using System.Windows;
using System.Windows.Input;

namespace FocusGuard.App.Views;

public partial class MasterKeySetupDialog : Window
{
    public MasterKeySetupDialog()
    {
        InitializeComponent();
    }

    // Allow dragging the borderless window
    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        DragMove();
    }
}
