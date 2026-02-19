using System.Windows;
using FocusGuard.App.ViewModels;

namespace FocusGuard.App.Views;

public partial class MainWindow : Window
{
    public MainWindow(MainWindowViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
