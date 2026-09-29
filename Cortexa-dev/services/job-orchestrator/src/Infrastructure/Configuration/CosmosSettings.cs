namespace Cortexa.JobOrchestrator.Infrastructure.Configuration;

public sealed class CosmosSettings
{
    public string Uri { get; set; } = string.Empty;
    // Primary key for local/emulator Cosmos auth; empty uses DefaultAzureCredential.
    public string Key { get; set; } = string.Empty;
    public string Database { get; set; } = string.Empty;
    public string BatchesContainer { get; set; } = string.Empty;
    public string DocumentsContainer { get; set; } = string.Empty;
    public string ChunksContainer { get; set; } = string.Empty;
    public string ProvenanceMapsContainer { get; set; } = string.Empty;
    public string VerdictsContainer { get; set; } = string.Empty;
    public string HarvestingContainer { get; set; } = string.Empty;
    public string SeedingContainer { get; set; } = string.Empty;
    public string CandidatesContainer { get; set; } = string.Empty;
    public string EvidenceBundlesContainer { get; set; } = string.Empty;
    public string ConfigContainer { get; set; } = string.Empty;
    public int SagaDeleteDeadlineSeconds { get; set; } = 15;
}
