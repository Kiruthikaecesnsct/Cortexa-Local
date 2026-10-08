using Collector.Domain.Upload;
using Collector.Server.Application.Commands;

namespace Collector.Server.Application.Upload;

public static class UploadCommandMapper
{
    public static WriteKnowledgeBatchCommand Map(KnowledgeUploadRequest request, UploadMappingContext context) =>
        new()
        {
            BatchId = context.BatchId,
            Saga = BuildSaga(request, context),
            Collector = request.Collector,
            Documents = [.. request.Documents.Select(document => BuildDocument(document, context.BatchId))]
        };

    private static SagaMetadataInput BuildSaga(KnowledgeUploadRequest request, UploadMappingContext context) =>
        new()
        {
            BatchName = request.BatchName.Trim(),
            CreatedAt = context.CreatedAt,
            OrgId = context.Caller.OrgId,
            OwnerUserId = context.Caller.UserId,
            Engine = context.Engine,
            ExtractionModel = context.Models.ExtractionModel,
            PrimaryEvidenceModel = context.Models.PrimaryEvidenceModel,
            ScoringModel = context.Models.ScoringModel,
            SeedingModel = context.Models.SeedingModel,
            SeedingMode = context.Models.SeedingMode,
            CollectorProvider = request.Collector.Provider,
            CollectorModel = request.Collector.Model
        };

    private static BatchDocumentInput BuildDocument(UploadDocument document, string batchId) =>
        new()
        {
            DocumentId = DeterministicIds.DocumentId(batchId, Guid.Parse(document.ClientDocumentId)),
            Filename = FilenameSanitizer.Clean(document.Filename),
            SourceKind = document.SourceKind,
            Items = document.KnowledgeItems
        };
}
