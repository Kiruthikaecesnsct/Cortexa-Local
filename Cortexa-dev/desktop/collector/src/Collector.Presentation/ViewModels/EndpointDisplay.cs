using Collector.Application.Settings;
using Collector.Presentation.Resources;

namespace Collector.Presentation.ViewModels;

public static class EndpointDisplay
{
    public static string? HostOf(string? url)
    {
        if (EndpointSettingsRules.ValidateUrl(url) is not null || !Uri.TryCreate(url!.Trim(), UriKind.Absolute, out var uri))
        {
            return null;
        }

        return uri.IsDefaultPort ? uri.Host : $"{uri.Host}:{uri.Port}";
    }

    public static string? ErrorFor(string? url, EndpointField field)
    {
        if (EndpointSettingsRules.ValidateUrl(url) is null)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(url))
        {
            return field == EndpointField.GatewayUrl ? SettingsStrings.GatewayEmpty : SettingsStrings.CollectorEmpty;
        }

        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri))
        {
            return field == EndpointField.GatewayUrl
                ? SettingsStrings.GatewayNotAbsolute
                : SettingsStrings.CollectorNotAbsolute;
        }

        return uri.Scheme == Uri.UriSchemeHttp ? SettingsStrings.UrlHttpNotLocal : SettingsStrings.UrlOtherScheme;
    }
}
