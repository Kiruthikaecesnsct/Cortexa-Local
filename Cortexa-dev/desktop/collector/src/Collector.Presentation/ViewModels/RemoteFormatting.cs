using System.Globalization;

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

public static class AzureOrganizationParser
{
    private const string AzureHost = "dev.azure.com";
    private const string LegacyHostSuffix = ".visualstudio.com";

    public static string Parse(string? raw)
    {
        var text = raw?.Trim() ?? string.Empty;
        if (text.Length == 0)
        {
            return string.Empty;
        }

        var candidate = text.Contains("://", StringComparison.Ordinal) ? text : $"https://{text}";
        return Uri.TryCreate(candidate, UriKind.Absolute, out var uri) ? FromUri(uri, text) : text;
    }

    private static string FromUri(Uri uri, string original)
    {
        if (uri.Host.Equals(AzureHost, StringComparison.OrdinalIgnoreCase))
        {
            return FirstSegment(uri) ?? string.Empty;
        }

        if (uri.Host.EndsWith(LegacyHostSuffix, StringComparison.OrdinalIgnoreCase))
        {
            return uri.Host[..^LegacyHostSuffix.Length];
        }

        return original;
    }

    private static string? FirstSegment(Uri uri) =>
        uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
}
