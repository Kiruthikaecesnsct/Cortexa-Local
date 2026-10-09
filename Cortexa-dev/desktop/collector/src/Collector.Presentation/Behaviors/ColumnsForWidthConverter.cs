using System.Globalization;
using System.Windows.Data;

namespace Collector.Presentation.Behaviors;

public sealed class ColumnsForWidthConverter : IValueConverter
{
    public double MinItemWidth { get; set; }

    public double Gap { get; set; }

    public double Bleed { get; set; }

    public int MaxColumns { get; set; } = 1;

    public static int Columns(double width, double minItemWidth, double gap, int maxColumns)
    {
        var ceiling = Math.Max(1, maxColumns);
        if (double.IsNaN(width) || double.IsInfinity(width) || minItemWidth + gap <= 0)
        {
            return ceiling;
        }

        var fit = (int)Math.Floor((width + gap) / (minItemWidth + gap));
        return Math.Clamp(fit, 1, ceiling);
    }

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var width = value is double measured ? measured - Bleed : double.NaN;
        return Columns(width, MinItemWidth, Gap, MaxColumns);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
