using System.Windows;
using System.Windows.Input;

namespace FocusGuard.App.Views;

public partial class TimerOverlayWindow : Window
{
    public TimerOverlayWindow()
    {
        InitializeComponent();
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        DragMove();
    }
}
