using System.Windows.Controls;
using System.Windows.Input;
using FocusGuard.App.Models;
using FocusGuard.App.ViewModels;

namespace FocusGuard.App.Views;

public partial class CalendarView : UserControl
{
    public CalendarView()
    {
        InitializeComponent();
    }

    private void DayCell_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is Border { DataContext: CalendarDay day }
            && DataContext is CalendarViewModel vm)
        {
            vm.SelectDayCommand.Execute(day);
        }
    }
}
