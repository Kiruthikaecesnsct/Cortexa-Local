namespace Cortexa.JobOrchestrator.Application.Settings;

public sealed class ReconciliationOptions
{
    public int BlockerOrphanThreshold { get; set; } = 100;
    public int MaxReconcilesPerRun { get; set; } = 50;
    public int PageSize { get; set; } = 100;
    public bool ReconcileEnabled { get; set; } = false;
}
