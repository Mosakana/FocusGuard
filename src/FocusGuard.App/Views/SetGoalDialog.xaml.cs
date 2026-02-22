using System.Windows;

namespace FocusGuard.App.Views;

public partial class SetGoalDialog : Window
{
    public SetGoalDialog()
    {
        InitializeComponent();
    }

    private void OnConfirm(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }

    private void OnCancel(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
