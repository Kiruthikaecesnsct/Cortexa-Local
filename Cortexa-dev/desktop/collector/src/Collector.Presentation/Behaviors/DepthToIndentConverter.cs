using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Collector.Presentation.Behaviors;

public sealed class DepthToIndentConverter : IValueConverter
{
    public double Step { get; set; }

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var depth = value is int level && level > 0 ? level : 0;
        return new Thickness(depth * Step, 0, 0, 0);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
