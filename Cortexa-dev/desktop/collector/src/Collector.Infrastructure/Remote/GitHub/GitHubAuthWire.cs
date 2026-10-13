using System.Text.Json.Serialization;

namespace Collector.Infrastructure.Remote.GitHub;

internal sealed record GitHubDeviceCodeWire(
    string? DeviceCode,
    string? UserCode,
    string? VerificationUri,
    int? ExpiresIn,
    int? Interval,
    string? Error);

internal sealed record GitHubTokenWire(
    string? AccessToken,
    string? RefreshToken,
    int? ExpiresIn,
    int? RefreshTokenExpiresIn,
    int? Interval,
    string? Error);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower, PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(GitHubDeviceCodeWire))]
[JsonSerializable(typeof(GitHubTokenWire))]
internal sealed partial class GitHubAuthJson : JsonSerializerContext;
