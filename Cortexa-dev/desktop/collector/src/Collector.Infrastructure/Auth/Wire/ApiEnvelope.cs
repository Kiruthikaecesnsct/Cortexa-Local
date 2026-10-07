namespace Collector.Infrastructure.Auth.Wire;

internal sealed record ApiEnvelope<T>(bool Success, T? Data, string? ErrorCode, string? Message);
