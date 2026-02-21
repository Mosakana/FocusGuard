using System.Globalization;
using System.Windows.Data;

namespace FocusGuard.App.Converters;

/// <summary>
/// MultiValueConverter: returns true if the first value (index) is less than the second value (threshold).
/// Used for Pomodoro dot indicators: filled when index &lt; completedCount.
/// </summary>
public class IndexLessThanConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length == 2
            && values[0] is int index
            && values[1] is int threshold)
        {
            return index < threshold;
        }
        return false;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
