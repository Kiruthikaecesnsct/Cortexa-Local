namespace Cortexa.ModelRouter.Application.Configuration;

public sealed class ModelCatalogSettings
{
    public List<ModelCatalogEntry> Models { get; set; } = new();
    public string SingleDefault { get; set; } = string.Empty;
    public Dictionary<string, string> Aliases { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class ModelCatalogEntry
{
    public string Id { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string Provider { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public bool Enabled { get; set; }
    public List<string> Capabilities { get; set; } = new();
    public List<string> AllowedStages { get; set; } = new();
    public string? Deployment { get; set; }
}
