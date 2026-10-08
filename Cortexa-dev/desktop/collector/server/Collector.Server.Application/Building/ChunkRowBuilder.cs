using Collector.Domain.Knowledge;
using Collector.Domain.Upload;
using Collector.Server.Application.Commands;
using Collector.Server.Application.Rows;
using Collector.Server.Application.Upload;

namespace Collector.Server.Application.Building;

public static class ChunkRowBuilder
{
    public static string BuildChunkId(string documentId, int orderIndex) =>
        $"{documentId}|{orderIndex}";

    public static IReadOnlyList<ChunkRow> Build(string batchId, BatchDocumentInput document, CollectorInfo collector)
    {
        var scope = new ChunkScope(batchId, document.DocumentId, collector);
        var rows = new List<ChunkRow>(document.Items.Count);
        var startChar = 0;

        for (var orderIndex = 0; orderIndex < document.Items.Count; orderIndex++)
        {
            var row = BuildRow(scope, document.Items[orderIndex], orderIndex, startChar);
            rows.Add(row);
            startChar = row.EndChar;
        }

        return rows;
    }

    private static ChunkRow BuildRow(ChunkScope scope, KnowledgeItem item, int orderIndex, int startChar)
    {
        var text = ChunkTextRenderer.Render(item);

        return new ChunkRow
        {
            Id = BuildChunkId(scope.DocumentId, orderIndex),
            BatchId = scope.BatchId,
            DocumentId = scope.DocumentId,
            Text = text,
            OrderIndex = orderIndex,
            StartChar = startChar,
            EndChar = startChar + text.Length,
            TokenCount = TokenEstimator.Estimate(text),
            PageNumber = item.Source.PageNumber,
            SectionHint = ResolveSectionHint(item),
            Knowledge = BuildKnowledge(item, scope.Collector)
        };
    }

    private static string? ResolveSectionHint(KnowledgeItem item)
    {
        if (UploadLimits.IsDocumentLevel(item.Kind))
        {
            return null;
        }

        return string.IsNullOrWhiteSpace(item.Source.Section) ? item.Title : item.Source.Section;
    }

    private static ChunkKnowledge BuildKnowledge(KnowledgeItem item, CollectorInfo collector) => new()
    {
        Kind = item.Kind,
        UnitKind = item.UnitKind,
        Title = item.Title,
        Summary = item.Summary,
        Details = item.Details,
        Excerpt = item.Excerpt,
        Source = item.Source,
        Provider = collector.Provider,
        Model = collector.Model,
        PromptVersion = collector.PromptVersion
    };

    private readonly record struct ChunkScope(string BatchId, string DocumentId, CollectorInfo Collector);
}
