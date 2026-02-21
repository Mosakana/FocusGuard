using System.Windows;
using System.Windows.Input;

namespace FocusGuard.App.Views;

public partial class StartSessionDialog : Window
{
    public StartSessionDialog()
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
