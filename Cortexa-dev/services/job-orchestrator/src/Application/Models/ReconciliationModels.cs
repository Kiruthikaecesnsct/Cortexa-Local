using Cortexa.JobOrchestrator.Application.Interfaces;

namespace Cortexa.JobOrchestrator.Application.Models;

public sealed record OrphanFinding(string StoreName, string BatchId, string ArtifactKind, string? Detail);

public sealed record StoreOrphanSummary(string StoreName, int OrphanCount, IReadOnlyList<string> BatchIds);

public sealed record DanglingReference(string BatchId, string RefKind, string RefId);

public enum ReconciliationSeverity { Info, Warning, Blocker }

public sealed class ReconciliationAccumulator
{
    public List<StoreOrphanSummary> PerStore { get; } = [];
    public List<DanglingReference> Dangling { get; } = [];
    public int ReconciledCount { get; set; }
    public Dictionary<string, int> PerStoreDeleted { get; } = [];
    public List<string> Errors { get; } = [];
}

public sealed record ReconciliationScannerDependencies(
    IEnumerable<IOrphanScanner> Scanners,
    IBatchExistenceQuery ExistenceQuery,
    IServiceBusStuckScanner StuckScanner,
    ISagaReferenceScanner ReferenceScanner);

public sealed record ReconciliationReport(
    DateTimeOffset StartedUtc,
    bool DryRun,
    IReadOnlyList<StoreOrphanSummary> PerStore,
    IReadOnlyList<DanglingReference> Dangling,
    int TotalOrphanBatches,
    ReconciliationSeverity Severity,
    int ReconciledCount,
    IReadOnlyDictionary<string, int> PerStoreDeleted,
    IReadOnlyList<string> Errors,
    TimeSpan Elapsed);
