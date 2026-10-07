using System.Text.Json;
using Collector.Application.Knowledge;
using Collector.Domain.Serialization;
using Collector.Domain.Upload;

namespace Collector.Application.Upload;

public enum BlockReason
{
    TooManyItems,
    TooLarge,
    DocumentMissing,
}

public sealed record BlockedDocument(string DocumentId, string Filename, int ItemCount, BlockReason Reason);

public sealed record PlannedBatch(int Index, KnowledgeUploadRequest Request, int ItemCount)
{
    public int DocumentCount => Request.Documents.Count;
}

public sealed record UploadPlan(IReadOnlyList<PlannedBatch> Batches, IReadOnlyList<BlockedDocument> Blocked);

public sealed class UploadBatchPlanner
{
    private const int CommaBytes = 1;

    public UploadPlan Plan(IReadOnlyList<UploadDocument> documents, CollectorInfo collector, string batchName)
    {
        var blocked = new List<BlockedDocument>();
        var groups = new List<List<UploadDocument>>();
        var envelope = SizeOf(Envelope(batchName, collector, []));
        var currentBytes = envelope;

        foreach (var document in documents)
        {
            var block = BlockFor(document, envelope);
            if (block is not null)
            {
                blocked.Add(block);
                continue;
            }

            var bytes = SizeOf(document) + CommaBytes;
            if (groups.Count == 0 || !Fits(groups[^1], currentBytes, bytes))
            {
                groups.Add([]);
                currentBytes = envelope;
            }

            groups[^1].Add(document);
            currentBytes += bytes;
        }

        return new UploadPlan(BuildBatches(groups, collector, batchName), blocked);
    }

    private static bool Fits(List<UploadDocument> group, int currentBytes, int documentBytes) =>
        group.Count < UploadLimitsMirror.MaxDocumentsPerBatch
        && currentBytes + documentBytes <= UploadLimitsMirror.MaxBodyBytes;

    private static BlockedDocument? BlockFor(UploadDocument document, int envelopeBytes)
    {
        if (document.KnowledgeItems.Count > UploadLimitsMirror.MaxItemsPerDocument)
        {
            return Blocked(document, BlockReason.TooManyItems);
        }

        var tooLarge = envelopeBytes + SizeOf(document) > UploadLimitsMirror.MaxBodyBytes;
        return tooLarge ? Blocked(document, BlockReason.TooLarge) : null;
    }

    private static BlockedDocument Blocked(UploadDocument document, BlockReason reason) =>
        new(document.ClientDocumentId, document.Filename, document.KnowledgeItems.Count, reason);

    private static List<PlannedBatch> BuildBatches(List<List<UploadDocument>> groups, CollectorInfo collector, string batchName)
    {
        var batches = new List<PlannedBatch>(groups.Count);
        for (var index = 0; index < groups.Count; index++)
        {
            var name = groups.Count == 1 ? batchName : NumberedName(batchName, index + 1, groups.Count);
            var request = Envelope(name, collector, groups[index]);
            batches.Add(new PlannedBatch(index, request, groups[index].Sum(document => document.KnowledgeItems.Count)));
        }

        return batches;
    }

    private static string NumberedName(string batchName, int number, int total)
    {
        var suffix = $" ({number} of {total})";
        var room = UploadLimitsMirror.BatchNameMax - suffix.Length;
        return UploadFieldClamp.Truncate(batchName, room) + suffix;
    }

    private static KnowledgeUploadRequest Envelope(string name, CollectorInfo collector, IReadOnlyList<UploadDocument> documents) => new()
    {
        BatchName = name,
        Collector = collector,
        Documents = documents,
    };

    private static int SizeOf<T>(T value) => JsonSerializer.SerializeToUtf8Bytes(value, CollectorJson.Options).Length;
}
