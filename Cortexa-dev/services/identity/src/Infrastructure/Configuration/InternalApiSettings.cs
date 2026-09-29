namespace Cortexa.Identity.Infrastructure.Configuration;

public sealed class InternalApiSettings
{
    public string SharedSecret { get; set; } = string.Empty;

    /// <summary>
    /// Name of the HTTP header the gateway sends the shared secret in.
    /// Must match the gateway's UserStatus:InternalKeyHeaderName configuration.
    /// </summary>
    public string HeaderName { get; set; } = "X-Internal-Key";
}
