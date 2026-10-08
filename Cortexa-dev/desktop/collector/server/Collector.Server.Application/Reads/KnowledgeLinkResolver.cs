using Collector.Domain.History;
using Collector.Server.Application.Building;
using Collector.Server.Application.Rows;

namespace Collector.Server.Application.Reads;

public static class KnowledgeLinkResolver
{
    public static IReadOnlyDictionary<string, ChunkKnowledgeRow> IndexChunks(IEnumerable<ChunkKnowledgeRow> chunks)
    {
        var index = new Dictionary<string, ChunkKnowledgeRow>(StringComparer.Ordinal);
        foreach (var chunk in chunks)
        {
            index[chunk.Id] = chunk;
        }

        return index;
    }

    public static IReadOnlyList<string> ChunkIdsOf(IEnumerable<ProvenanceLinkRow> links)
    {
        var chunkIds = new List<string>();
        foreach (var link in links)
        {
            var chunkId = ResolveChunkId(link);
            if (chunkId is not null)
            {
                chunkIds.Add(chunkId);
            }
        }

        return chunkIds;
    }

    public static IReadOnlyList<CandidateKnowledgeLink> Resolve(
        IEnumerable<string> chunkIds,
        IReadOnlyDictionary<string, ChunkKnowledgeRow> chunkIndex)
    {
        var links = new List<CandidateKnowledgeLink>();
        foreach (var chunkId in chunkIds.Distinct(StringComparer.Ordinal))
        {
            if (chunkIndex.TryGetValue(chunkId, out var chunk))
            {
                links.Add(ToLink(chunk));
            }
        }

        return links;
    }

    private static string? ResolveChunkId(ProvenanceLinkRow link)
    {
        if (!string.IsNullOrEmpty(link.ChunkId))
        {
            return link.ChunkId;
        }

        if (link.DocumentId is not null && link.SourceChunkIndex is not null)
        {
            return ChunkRowBuilder.BuildChunkId(link.DocumentId, link.SourceChunkIndex.Value);
        }

        return null;
    }

    private static CandidateKnowledgeLink ToLink(ChunkKnowledgeRow chunk) => new()
    {
        KnowledgeItem = new LinkedKnowledgeItem
        {
            Id = chunk.Id,
            Kind = chunk.Knowledge.Kind,
            Title = chunk.Knowledge.Title,
            Summary = chunk.Knowledge.Summary
        },
        Source = new CandidateSource
        {
            DocumentId = chunk.DocumentId,
            PageNumber = chunk.Knowledge.Source.PageNumber,
            FilePath = chunk.Knowledge.Source.FilePath,
            LineStart = chunk.Knowledge.Source.LineStart,
            LineEnd = chunk.Knowledge.Source.LineEnd
        }
    };
}
