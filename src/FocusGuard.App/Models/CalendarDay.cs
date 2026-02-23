using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace FocusGuard.App.Models;

public partial class CalendarDay : ObservableObject
{
    public DateTime Date { get; init; }
    public bool IsCurrentMonth { get; init; }
    public bool IsToday { get; init; }
    public ObservableCollection<CalendarTimeBlock> TimeBlocks { get; init; } = [];

    [ObservableProperty]
    private bool _isSelected;
}
