using System.Windows;
using System.Windows.Input;

namespace FocusGuard.App.Views;

public partial class ScheduleSessionDialog : Window
{
    public ScheduleSessionDialog()
    {
        InitializeComponent();
    }

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        DragMove();
    }
}
