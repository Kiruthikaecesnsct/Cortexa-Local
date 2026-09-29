namespace Cortexa.JobOrchestrator.Application.Settings;

public sealed class OrchestratorSettings
{
    public int ConcurrencyCap { get; set; } = 5;
    public double EvidenceSourceOutageThreshold { get; set; } = 0.0;
    public int MaxChunksPerExtractionUnit { get; set; } = 25;
}
