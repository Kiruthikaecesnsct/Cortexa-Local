namespace Cortexa.JobOrchestrator.Application.Settings;

public sealed class RetentionPolicyOptions
{
    public bool Enabled { get; set; } = true;
    public int IntervalHours { get; set; } = 24;
    public int FailedRetentionDays { get; set; } = 30;
    public int IncompleteStuckHours { get; set; } = 72;
    public bool DryRun { get; set; } = true;
    public bool ForceWedgedRunning { get; set; } = false;
    public int BatchPageSize { get; set; } = 100;
    public int MaxDeletesPerRun { get; set; } = 200;
}
