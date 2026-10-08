using Collector.Domain.Enums;

namespace Collector.Server.Application.Commands;

public sealed record SagaMetadataInput
{
    public required string BatchName { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public required string OrgId { get; init; }

    public required string OwnerUserId { get; init; }

    public required string Engine { get; init; }

    public required string ExtractionModel { get; init; }

    public required string PrimaryEvidenceModel { get; init; }

    public required string ScoringModel { get; init; }

    public required string SeedingModel { get; init; }

    public required string SeedingMode { get; init; }

    public required CollectorProvider CollectorProvider { get; init; }

    public required string CollectorModel { get; init; }
}
