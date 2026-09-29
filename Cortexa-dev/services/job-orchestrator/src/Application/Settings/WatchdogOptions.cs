namespace Cortexa.JobOrchestrator.Application.Settings;

public sealed class WatchdogOptions
{
    public bool WatchdogEnabled { get; set; } = false;
    public int WatchdogIntervalMinutes { get; set; }
    public int StageStallSlaMinutes { get; set; } = 30;
}
