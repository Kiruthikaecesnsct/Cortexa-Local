namespace Collector.Infrastructure.Auth.Wire;

internal sealed record TokenData(string? AccessToken, DateTimeOffset? ExpiresAt);
