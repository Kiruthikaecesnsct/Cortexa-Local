namespace Cortexa.JobOrchestrator.Infrastructure.Configuration;

public sealed class RabbitMqSettings
{
    // AMQP URI (amqp://user:password@host:port/vhost), supplied through configuration.
    public string Uri { get; set; } = string.Empty;
    public string ClientName { get; set; } = "job-orchestrator";
    public ushort PrefetchCount { get; set; } = 1;
    // Upper bound on messages inspected per queue by the dead-letter scanner and batch deleter.
    public int MaxScanMessages { get; set; } = 500;
}
