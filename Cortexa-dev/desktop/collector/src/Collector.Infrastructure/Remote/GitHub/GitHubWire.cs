using System.Text.Json.Serialization;

namespace Collector.Infrastructure.Remote.GitHub;

internal sealed record GitHubOwnerWire(string? Login);

internal sealed record GitHubRepoWire(
    string? Name,
    string? FullName,
    string? HtmlUrl,
    string? DefaultBranch,
    long Size,
    [property: JsonPropertyName("private")] bool IsPrivate,
    GitHubOwnerWire? Owner);

internal sealed record GitHubCommitWire(string? Sha);

internal sealed record GitHubBranchWire(string? Name, GitHubCommitWire? Commit);

internal sealed record GitHubRefObjectWire(string? Sha);

internal sealed record GitHubRefWire(GitHubRefObjectWire? Object);

internal sealed record GitHubTreeItemWire(string? Path, string? Type, string? Sha, long? Size);

internal sealed record GitHubTreeWire(string? Sha, bool Truncated, List<GitHubTreeItemWire>? Tree);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower, PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(List<GitHubRepoWire>))]
[JsonSerializable(typeof(List<GitHubBranchWire>))]
[JsonSerializable(typeof(GitHubRefWire))]
[JsonSerializable(typeof(GitHubTreeWire))]
internal sealed partial class GitHubJson : JsonSerializerContext;
