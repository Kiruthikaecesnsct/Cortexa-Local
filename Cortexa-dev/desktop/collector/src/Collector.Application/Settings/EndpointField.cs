namespace Collector.Application.Settings;

public enum EndpointField
{
    GatewayUrl,
    CollectorServerUrl,
}

public sealed record SettingsSaveResult(EndpointField? Field, string? Reason)
{
    public bool IsValid => Field is null;

    public static SettingsSaveResult Ok { get; } = new(null, null);

    public static SettingsSaveResult Invalid(EndpointField field, string reason) => new(field, reason);
}
