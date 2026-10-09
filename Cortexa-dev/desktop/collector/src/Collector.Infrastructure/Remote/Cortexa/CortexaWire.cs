using System.Text.Json.Serialization;

namespace Collector.Infrastructure.Remote.Cortexa;

internal sealed record CortexaEnvelopeWire<T>(T? Data);

internal sealed record CortexaCloneWire(
    string? CloneId,
    string? Provider,
    string? Owner,
    string? Repository,
    string? Branch,
    string? Status,
    string? CreatedAt,
    string? UpdatedAt,
    long? SizeBytes,
    string? CommitSha,
    string? Error);

internal sealed record CortexaCloneListWire(List<CortexaCloneWire>? Clones);

internal sealed record CortexaFilesWire(List<string>? Files);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower, PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(CortexaEnvelopeWire<CortexaCloneListWire>))]
[JsonSerializable(typeof(CortexaEnvelopeWire<CortexaFilesWire>))]
internal sealed partial class CortexaJson : JsonSerializerContext;
