namespace Collector.Server.Application.Rows;

public sealed record BatchResultJoinRow
{
    public required string Engine { get; init; }

    public string? ChunkId { get; init; }

    public string? DocumentId { get; init; }

    public int? SourceChunkIndex { get; init; }
}
