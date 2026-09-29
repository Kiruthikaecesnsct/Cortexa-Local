namespace Cortexa.JobOrchestrator.Infrastructure.Configuration;

public sealed class BlobSettings
{
    public string AccountUrl { get; set; } = string.Empty;
    public string RawContainer { get; set; } = string.Empty;
    public string ViewableContainer { get; set; } = "viewable-docs";
}
