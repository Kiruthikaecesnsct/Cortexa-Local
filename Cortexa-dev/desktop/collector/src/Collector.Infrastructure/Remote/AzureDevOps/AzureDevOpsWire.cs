using System.Text.Json.Serialization;

namespace Collector.Infrastructure.Remote.AzureDevOps;

internal sealed record AzureDevOpsListWire<T>(List<T>? Value);

internal sealed record AzureDevOpsProjectWire(string? Name, string? Visibility);

internal sealed record AzureDevOpsRepoWire(
    string? Name,
    string? DefaultBranch,
    string? WebUrl,
    long? Size,
    bool? IsDisabled,
    AzureDevOpsProjectWire? Project);

internal sealed record AzureDevOpsRefWire(string? Name, string? ObjectId);

internal sealed record AzureDevOpsItemWire(
    string? ObjectId,
    string? GitObjectType,
    string? CommitId,
    string? Path,
    bool? IsFolder);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(AzureDevOpsListWire<AzureDevOpsRepoWire>))]
[JsonSerializable(typeof(AzureDevOpsListWire<AzureDevOpsRefWire>))]
[JsonSerializable(typeof(AzureDevOpsListWire<AzureDevOpsItemWire>))]
internal sealed partial class AzureDevOpsJson : JsonSerializerContext;
