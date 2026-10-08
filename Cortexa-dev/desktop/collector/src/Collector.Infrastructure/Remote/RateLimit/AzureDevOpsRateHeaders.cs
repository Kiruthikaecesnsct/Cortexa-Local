namespace Collector.Infrastructure.Remote.RateLimit;

public sealed class AzureDevOpsRateHeaders() : RateHeaderReaderBase("X-RateLimit-Remaining", "X-RateLimit-Reset");
