using System.Collections.ObjectModel;

namespace FocusGuard.App.Models;

public class CalendarDay
{
    public DateTime Date { get; init; }
    public bool IsCurrentMonth { get; init; }
    public bool IsToday { get; init; }
    public ObservableCollection<CalendarTimeBlock> TimeBlocks { get; init; } = [];
}
