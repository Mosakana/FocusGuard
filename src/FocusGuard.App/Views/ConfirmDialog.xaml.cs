using System.Windows;
using System.Windows.Input;

namespace FocusGuard.App.Views;

public partial class ConfirmDialog : Window
{
    public ConfirmDialog(string title, string message)
    {
        InitializeComponent();
        TitleText.Text = title;
        MessageText.Text = message;
    }

    private void OnDragMove(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
            DragMove();
    }

    private void OnYes(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }

    private void OnNo(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
