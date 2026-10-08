using Microsoft.Azure.Cosmos;

namespace Collector.Server.Infrastructure.Options;

public sealed class CosmosOptions
{
    public const string SectionName = "Cosmos";

    public string Endpoint { get; set; } = string.Empty;

    public string Key { get; set; } = string.Empty;

    public string Database { get; set; } = string.Empty;

    public string DocumentsContainer { get; set; } = string.Empty;

    public string ChunksContainer { get; set; } = string.Empty;

    public string ProvenanceMapsContainer { get; set; } = string.Empty;

    public string BatchesContainer { get; set; } = string.Empty;

    public string ConfigContainer { get; set; } = string.Empty;

    public string ReportsContainer { get; set; } = string.Empty;

    public string VerdictsContainer { get; set; } = string.Empty;

    public string EvidenceBundlesContainer { get; set; } = string.Empty;

    public ConnectionMode ConnectionMode { get; set; } = ConnectionMode.Direct;

    public bool LimitToEndpoint { get; set; }

    public int MaxConcurrentWrites { get; set; }
}
