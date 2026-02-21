using System.Globalization;
using System.Windows.Data;

namespace FocusGuard.App.Converters;

/// <summary>
/// Converts an integer to an enumerable range [0..N-1] for use with ItemsControl.
/// Used to generate Pomodoro dot indicators.
/// </summary>
public class IntToRangeConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is int count && count > 0)
        {
            return Enumerable.Range(0, Math.Min(count, 20)).ToList();
        }
        return Array.Empty<int>();
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
