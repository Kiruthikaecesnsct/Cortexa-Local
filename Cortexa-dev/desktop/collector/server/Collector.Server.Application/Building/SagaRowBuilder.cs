using Collector.Server.Application.Commands;
using Collector.Server.Application.Rows;

namespace Collector.Server.Application.Building;

public static class SagaRowBuilder
{
    private const string EngineHarvesting = "harvesting";
    private const string EngineSeeding = "seeding";
    private const string EngineDual = "dual";

    public static SagaRow Build(WriteKnowledgeBatchCommand command)
    {
        var metadata = command.Saga;

        return new SagaRow
        {
            Id = command.BatchId,
            BatchId = command.BatchId,
            Documents = [.. command.Documents.Select(BuildDocumentProgress)],
            WantsHarvesting = WantsHarvesting(metadata.Engine),
            WantsSeeding = WantsSeeding(metadata.Engine),
            BatchName = metadata.BatchName,
            Engine = metadata.Engine,
            CreatedAt = metadata.CreatedAt,
            TotalDocumentCount = command.Documents.Count,
            OrgId = metadata.OrgId,
            OwnerUserId = metadata.OwnerUserId,
            ExtractionModel = metadata.ExtractionModel,
            PrimaryEvidenceModel = metadata.PrimaryEvidenceModel,
            ScoringModel = metadata.ScoringModel,
            SeedingModel = metadata.SeedingModel,
            SeedingMode = metadata.SeedingMode
        };
    }

    public static bool WantsHarvesting(string engine) =>
        IsEngine(engine, EngineHarvesting) || IsEngine(engine, EngineDual);

    public static bool WantsSeeding(string engine) =>
        IsEngine(engine, EngineSeeding) || IsEngine(engine, EngineDual);

    private static SagaDocumentProgressRow BuildDocumentProgress(BatchDocumentInput document) =>
        new() { DocumentId = document.DocumentId };

    private static bool IsEngine(string engine, string expected) =>
        string.Equals(engine, expected, StringComparison.OrdinalIgnoreCase);
}
