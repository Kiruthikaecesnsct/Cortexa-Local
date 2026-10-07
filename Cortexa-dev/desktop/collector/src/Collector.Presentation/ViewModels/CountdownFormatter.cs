using System.Globalization;
using Collector.Presentation.Resources;

namespace Collector.Presentation.ViewModels;

public static class CountdownFormatter
{
    private const int SecondsPerMinute = 60;
    private const int SecondsPerHour = 3600;

    public static string Format(TimeSpan remaining)
    {
        var total = WholeSeconds(remaining);
        var hours = total / SecondsPerHour;
        var minutes = total % SecondsPerHour / SecondsPerMinute;
        var seconds = total % SecondsPerMinute;
        return hours > 0
            ? string.Create(CultureInfo.InvariantCulture, $"{hours}:{minutes:00}:{seconds:00}")
            : string.Create(CultureInfo.InvariantCulture, $"{minutes}:{seconds:00}");
    }

    public static string Approximate(TimeSpan remaining)
    {
        var total = WholeSeconds(remaining);
        return total < SecondsPerMinute
            ? SignInStrings.LessThanAMinute
            : SignInStrings.Approximately((int)Math.Round(total / (double)SecondsPerMinute));
    }

    private static int WholeSeconds(TimeSpan remaining) =>
        (int)Math.Max(0, Math.Ceiling(remaining.TotalSeconds));
}
