using Collector.Server.Application.Rows;

namespace Collector.Server.Tests.Fakes;

internal static class TestSagas
{
    public static SagaRow Existing(string batchId, params string[] documentIds) => new()
    {
        Id = batchId,
        BatchId = batchId,
        Documents = [.. documentIds.Select(id => new SagaDocumentProgressRow { DocumentId = id })],
        WantsHarvesting = true,
        WantsSeeding = true,
        BatchName = "Existing",
        Engine = TestData.DualEngine,
        CreatedAt = TestData.FixedTime,
        TotalDocumentCount = documentIds.Length,
        OrgId = TestIdentity.OrgId,
        OwnerUserId = TestIdentity.UserId,
        ExtractionModel = "m1",
        PrimaryEvidenceModel = "m2",
        ScoringModel = "m3",
        SeedingModel = "m4",
        SeedingMode = "legacy"
    };

    public static SagaRow WithDocumentStates(
        string batchId,
        bool wantsHarvesting,
        bool wantsSeeding,
        params string[] documentStates) =>
        Existing(batchId, [.. documentStates.Select((_, index) => $"doc-{index}")]) with
        {
            WantsHarvesting = wantsHarvesting,
            WantsSeeding = wantsSeeding,
            Documents =
            [
                .. documentStates.Select((state, index) => new SagaDocumentProgressRow
                {
                    DocumentId = $"doc-{index}",
                    State = state
                })
            ]
        };

    public static SagaRow WithDocumentState(SagaRow saga, string documentId, string state) =>
        saga with
        {
            Documents =
            [
                .. saga.Documents.Select(document =>
                    document.DocumentId == documentId ? document with { State = state } : document)
            ]
        };

    public static DocumentRow DocumentFor(string batchId, string documentId) => new()
    {
        Id = documentId,
        BatchId = batchId,
        Filename = "seeded.pdf",
        SourceKind = Collector.Domain.Enums.SourceKind.Paper,
        ProvenanceMapId = $"{documentId}|0",
        ChunkCount = 1,
        CreatedAt = TestData.FixedTime
    };
}
