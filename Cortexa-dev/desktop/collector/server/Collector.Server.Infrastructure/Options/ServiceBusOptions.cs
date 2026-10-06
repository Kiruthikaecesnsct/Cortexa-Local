namespace Collector.Server.Infrastructure.Options;

public sealed class ServiceBusOptions
{
    public string ConnectionString { get; set; } = string.Empty;

    public string FullyQualifiedNamespace { get; set; } = string.Empty;
}
