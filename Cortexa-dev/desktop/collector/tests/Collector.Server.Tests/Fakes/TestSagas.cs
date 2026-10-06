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
}
