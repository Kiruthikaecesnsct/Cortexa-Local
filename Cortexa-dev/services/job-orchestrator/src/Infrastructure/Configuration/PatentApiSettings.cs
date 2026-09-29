namespace Cortexa.JobOrchestrator.Infrastructure.Configuration;

// Non-secret base URLs for the live connection probe. Mirrors the defaults the
// evidence service uses for the same three patent APIs (see
// services/evidence/src/evidence/infrastructure/config/settings.py) so a probe
// success/failure matches what the evidence pipeline would actually experience.
public sealed class PatentApiSettings
{
    public string UsptoBaseUrl { get; set; } = "https://api.uspto.gov";
    public string EpoBaseUrl { get; set; } = "https://ops.epo.org";
    public string EpoTokenPath { get; set; } = "/3.2/auth/accesstoken";
    public string LensBaseUrl { get; set; } = "https://api.lens.org";
    public int TimeoutSeconds { get; set; } = 15;
}
