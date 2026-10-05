namespace Cortexa.JobOrchestrator.Application.Models;

/// <summary>
/// A branch saved to repository storage by the ingestion service. A batch document
/// carrying one is ingested from that saved folder instead of an upload or a clone.
/// Provider is "github" or "azure-devops"; Azure DevOps repositories are "project/repo".
/// </summary>
public sealed record SavedRepositoryRef(string Provider, string Owner, string Repository, string Branch)
{
    /// <summary>Repo-relative paths to ingest. Null means the whole saved folder.</summary>
    public IReadOnlyList<string>? SelectedFiles { get; init; }

    public string DisplayName => $"{Owner}-{Repository.Replace('/', '-')}@{Branch}";
}
