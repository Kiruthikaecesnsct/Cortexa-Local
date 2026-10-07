namespace Collector.Application.Settings;

public static class EndpointSettingsRules
{
    private static readonly string[] LocalHosts = ["localhost", "127.0.0.1"];

    public static string? ValidateUrl(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || !Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri))
        {
            return "Enter a full URL, for example https://host.";
        }

        return IsAllowedScheme(uri) ? null : "Use https. Plain http is allowed only for localhost.";
    }

    public static SettingsSaveResult Validate(EndpointSettings settings)
    {
        var gateway = ValidateUrl(settings.GatewayUrl);
        if (gateway is not null)
        {
            return SettingsSaveResult.Invalid(EndpointField.GatewayUrl, gateway);
        }

        var server = ValidateUrl(settings.CollectorServerUrl);
        return server is null
            ? SettingsSaveResult.Ok
            : SettingsSaveResult.Invalid(EndpointField.CollectorServerUrl, server);
    }

    private static bool IsAllowedScheme(Uri uri) =>
        uri.Scheme == Uri.UriSchemeHttps
        || (uri.Scheme == Uri.UriSchemeHttp && LocalHosts.Contains(uri.Host, StringComparer.OrdinalIgnoreCase));
}
