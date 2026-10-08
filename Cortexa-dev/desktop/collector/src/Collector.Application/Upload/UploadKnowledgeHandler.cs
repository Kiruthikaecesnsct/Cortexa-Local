using Collector.Application.Knowledge;
using Collector.Application.Ports;
using Collector.Domain.Documents;
using Collector.Domain.Enums;
using Collector.Domain.Upload;

namespace Collector.Application.Upload;

public sealed class UploadKnowledgeHandler(
    IDocumentStore documentStore,
    IBatchStore batchStore,
    BatchSender sender,
    UploadBatchPlanner planner,
    TimeProvider timeProvider)
{
    public async Task<UploadSession> PrepareAsync(UploadRequest request, CancellationToken cancellationToken)
    {
        var records = new List<PlannedBatchRecord>();
        var blocked = new List<BlockedDocument>();
        var missingSeen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var versionGroup in request.Items.GroupBy(item => item.PromptVersion, StringComparer.Ordinal))
        {
            var promptVersion = versionGroup.Key;
            var collector = new CollectorInfo
            {
                AppVersion = request.AppVersion,
                Provider = request.Provider,
                Model = request.Model,
                PromptVersion = promptVersion,
            };
            var (documents, missing) = await BuildDocumentsAsync([.. versionGroup], cancellationToken);
            foreach (var missingDocument in missing)
            {
                if (missingSeen.Add(missingDocument.DocumentId))
                {
                    blocked.Add(missingDocument);
                }
            }

            var plan = planner.Plan(documents, collector, BatchNameFor(request));
            blocked.AddRange(plan.Blocked);
            records.AddRange(await PersistAsync(plan, request, promptVersion, cancellationToken));
        }

        return new UploadSession(records, blocked, sender);
    }

    private string BatchNameFor(UploadRequest request) =>
        UploadFieldClamp.Truncate(
            string.IsNullOrWhiteSpace(request.BatchName)
                ? $"Collector upload {timeProvider.GetUtcNow():yyyy-MM-dd HH:mm}"
                : request.BatchName,
            UploadLimitsMirror.BatchNameMax);

    private async Task<(List<UploadDocument> Documents, List<BlockedDocument> Missing)> BuildDocumentsAsync(
        IReadOnlyList<ExtractedKnowledgeItem> items,
        CancellationToken cancellationToken)
    {
        var documents = new List<UploadDocument>();
        var missing = new List<BlockedDocument>();
        foreach (var group in items.GroupBy(item => item.DocumentId))
        {
            var document = await documentStore.GetAsync(group.Key, cancellationToken);
            if (document is null)
            {
                missing.Add(new BlockedDocument(group.Key, group.First().DocumentName, group.Count(), BlockReason.DocumentMissing));
                continue;
            }

            documents.Add(ToUploadDocument(document, group));
        }

        return (documents, missing);
    }

    private static UploadDocument ToUploadDocument(CollectorDocument document, IEnumerable<ExtractedKnowledgeItem> items) => new()
    {
        ClientDocumentId = document.Id,
        Filename = UploadFieldClamp.Truncate(document.Filename, UploadLimitsMirror.FilenameMax),
        SourceKind = document.SourceKind,
        SourceType = document.SourceType,
        KnowledgeItems = [.. items.Select(UploadFieldClamp.ToKnowledgeItem)],
    };

    private async Task<List<PlannedBatchRecord>> PersistAsync(
        UploadPlan plan,
        UploadRequest request,
        string promptVersion,
        CancellationToken cancellationToken)
    {
        var records = new List<PlannedBatchRecord>(plan.Batches.Count);
        foreach (var planned in plan.Batches)
        {
            var key = Guid.CreateVersion7().ToString();
            var payload = UploadPayload.From(planned.Request);
            var newBatch = NewBatchFor(planned, key, payload, request, promptVersion);
            await SupersedeOpenFailureAsync(newBatch.DocumentIds, cancellationToken);
            var stored = await batchStore.CreateAsync(newBatch, cancellationToken);
            records.Add(new PlannedBatchRecord(planned, key, stored.Id, payload));
        }

        return records;
    }

    private async Task SupersedeOpenFailureAsync(IReadOnlyList<string> documentIds, CancellationToken cancellationToken)
    {
        var existing = await batchStore.FindByDocumentsAsync(documentIds, cancellationToken);
        if (existing is { Status: BatchStatus.Failed, IsReplaced: false })
        {
            await batchStore.MarkReplacedAsync([existing.Id], cancellationToken);
        }
    }

    private static NewBatch NewBatchFor(
        PlannedBatch planned,
        string key,
        UploadPayload payload,
        UploadRequest request,
        string promptVersion) => new()
        {
            IdempotencyKey = key,
            BatchName = planned.Request.BatchName,
            Provider = request.Provider,
            Model = request.Model,
            PromptVersion = promptVersion,
            DocumentIds = [.. planned.Request.Documents.Select(document => document.ClientDocumentId)],
            Payload = payload,
        };
}
