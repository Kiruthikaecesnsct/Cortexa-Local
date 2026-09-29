namespace Cortexa.JobOrchestrator.Infrastructure.Configuration;

public sealed class BlobSettings
{
    public string AccountUrl { get; set; } = string.Empty;
    // Connection string for local/emulator Blob auth (e.g. Azurite); empty uses DefaultAzureCredential.
    public string ConnectionString { get; set; } = string.Empty;
    public string RawContainer { get; set; } = string.Empty;
    public string ViewableContainer { get; set; } = "viewable-docs";
}
