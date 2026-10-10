using System.Globalization;
using Collector.Application.Remote.Selection;
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

public static class RemoteSelectionText
{
    public static bool IsAllUnknown(FileSelectionSummary summary) =>
        summary.SupportedSelected > 0 && summary.UnknownSizeFiles == summary.SupportedSelected;

    public static bool HasUnknown(FileSelectionSummary summary) => summary.UnknownSizeFiles > 0;

    public static string SizePhrase(FileSelectionSummary summary)
    {
        if (IsAllUnknown(summary))
        {
            return RemoteSourceStrings.SizeUnknown;
        }

        var size = RemoteSizeFormatter.Format(summary.SelectedBytes);
        return HasUnknown(summary) ? RemoteSourceStrings.SizeAtLeast(size, summary.UnknownSizeFiles) : size;
    }

    public static string FooterText(FileSelectionSummary summary) => IsAllUnknown(summary)
        ? RemoteSourceStrings.FilesSummaryUnknownSize(summary.SupportedSelected, summary.SkippedUnsupported)
        : RemoteSourceStrings.FilesSummary(
            summary.SupportedSelected,
            SizePhrase(summary),
            summary.SkippedUnsupported,
            summary.SkippedTooLarge);

    public static string ProceedText(FileSelectionSummary summary) =>
        RemoteSourceStrings.SelectionText(summary.SupportedSelected, SizePhrase(summary));

    public static string SkippedLine(FileSelectionSummary summary)
    {
        if (IsAllUnknown(summary))
        {
            return summary.SkippedUnsupported == 0 ? string.Empty : RemoteSourceStrings.SkippedUnsupportedOnly(summary.SkippedUnsupported);
        }

        var skipped = summary.SkippedUnsupported + summary.SkippedTooLarge;
        return skipped == 0 ? string.Empty : RemoteSourceStrings.SkippedText(summary.SkippedUnsupported, summary.SkippedTooLarge);
    }
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

    public static string Describe(TimeSpan age)
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
