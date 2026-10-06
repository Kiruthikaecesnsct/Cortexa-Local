using Collector.Domain.Enums;
using Collector.Server.Application.Commands;
using Collector.Server.Application.Rows;

namespace Collector.Server.Application.Building;

public static class ProvenanceRowBuilder
{
    private const int DefaultLine = 1;

    public static IReadOnlyList<ProvenanceRow> Build(IReadOnlyList<ChunkRow> chunks, BatchDocumentInput document) =>
        [.. chunks.Select(chunk => BuildRow(chunk, document))];

    private static ProvenanceRow BuildRow(ChunkRow chunk, BatchDocumentInput document)
    {
        var row = new ProvenanceRow
        {
            Id = chunk.Id,
            BatchId = chunk.BatchId,
            ChunkId = chunk.Id,
            SourceKind = document.SourceKind,
            OrderIndex = chunk.OrderIndex,
            DocId = chunk.DocumentId
        };

        return document.SourceKind == SourceKind.Code
            ? WithCodeLocation(row, chunk, document.Filename)
            : row with { ByteRange = [chunk.StartChar, chunk.EndChar] };
    }

    private static ProvenanceRow WithCodeLocation(ProvenanceRow row, ChunkRow chunk, string filename)
    {
        var source = chunk.Knowledge.Source;
        var lineStart = source.LineStart ?? DefaultLine;
        var lineEnd = source.LineEnd ?? lineStart;

        return row with
        {
            FilePath = string.IsNullOrWhiteSpace(source.FilePath) ? filename : source.FilePath,
            LineRange = [lineStart, lineEnd]
        };
    }
}
