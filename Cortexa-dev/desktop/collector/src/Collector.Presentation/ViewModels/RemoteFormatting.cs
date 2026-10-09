using System.Globalization;
using Collector.Presentation.Resources;

namespace Collector.Presentation.ViewModels;

public static class RemoteSizeFormatter
{
    private const double BytesPerKilobyte = 1024;
    private const double KilobytesPerMegabyte = 1024;
    private const double MegabytesPerGigabyte = 1024;
    private const double OneDecimalBelowMegabytes = 100;

    public static string Format(long bytes)
    {
        var kilobytes = bytes / BytesPerKilobyte;
        var megabytes = kilobytes / KilobytesPerMegabyte;
        var gigabytes = megabytes / MegabytesPerGigabyte;
        if (megabytes < 1)
        {
            return string.Create(CultureInfo.InvariantCulture, $"{Round(kilobytes)} KB");
        }

        if (gigabytes >= 1)
        {
            return string.Create(CultureInfo.InvariantCulture, $"{gigabytes:0.0} GB");
        }

        return megabytes < OneDecimalBelowMegabytes
            ? string.Create(CultureInfo.InvariantCulture, $"{megabytes:0.0} MB")
            : string.Create(CultureInfo.InvariantCulture, $"{Round(megabytes)} MB");
    }

    private static double Round(double value) => Math.Round(value, MidpointRounding.AwayFromZero);
}

public static class RelativeTimeFormatter
{
    private const int MinutesPerHour = 60;
    private const int HoursPerDay = 24;
    private const int DaysPerMonth = 30;
    private const int DaysPerYear = 365;

    public static string Format(DateTimeOffset? updated, DateTimeOffset now)
    {
        if (updated is not { } value)
        {
            return string.Empty;
        }

        return RemoteSourceStrings.UpdatedText(Describe(now - value));
    }

    private static string Describe(TimeSpan age)
    {
        if (age.TotalMinutes < 1)
        {
            return "just now";
        }

        if (age.TotalMinutes < MinutesPerHour)
        {
            return Unit((int)age.TotalMinutes, "minute");
        }

        if (age.TotalHours < HoursPerDay)
        {
            return Unit((int)age.TotalHours, "hour");
        }

        return DescribeDays((int)age.TotalDays);
    }

    private static string DescribeDays(int days)
    {
        if (days < DaysPerMonth)
        {
            return Unit(days, "day");
        }

        return days < DaysPerYear ? Unit(days / DaysPerMonth, "month") : Unit(days / DaysPerYear, "year");
    }

    private static string Unit(int count, string name) => $"{count} {name}{(count == 1 ? string.Empty : "s")} ago";
}
