using Collector.Domain.Knowledge;
using Collector.Server.Application.Building;
using Collector.Server.Application.Ports;
using Collector.Server.Application.Reads;
using Collector.Server.Application.Rows;
using Collector.Server.Application.Upload;

namespace Collector.Server.Application.Handlers;

public sealed class GetBatchResultsHandler(IPipelineRowStore store)
{
    public async Task<GetBatchResultsOutcome> HandleAsync(
        string batchId,
        UploadCaller caller,
        CancellationToken cancellationToken)
    {
        var saga = await store.GetSagaAsync(batchId, cancellationToken);
        if (saga is null || !OwnedBy(saga, caller))
        {
            return GetBatchResultsOutcome.NotFound();
        }

        var chunks = await store.GetChunksByBatchAsync(batchId, cancellationToken);
        var joinRows = await store.GetResultsByBatchAsync(batchId, cancellationToken);
        var chunkIndex = chunks.ToDictionary(chunk => chunk.Id);

        var items = new List<BatchResultDto>();
        foreach (var joinRow in joinRows)
        {
            var dto = ToDto(joinRow, chunkIndex);
            if (dto is not null)
            {
                items.Add(dto);
            }
        }

        return GetBatchResultsOutcome.Ok(items);
    }

    private static bool OwnedBy(SagaRow saga, UploadCaller caller) =>
        string.Equals(saga.OwnerUserId, caller.UserId, StringComparison.Ordinal) &&
        string.Equals(saga.OrgId, caller.OrgId, StringComparison.Ordinal);

    private static BatchResultDto? ToDto(
        BatchResultJoinRow joinRow,
        IReadOnlyDictionary<string, ChunkRow> chunkIndex)
    {
        var chunkId = ResolveChunkId(joinRow);
        if (chunkId is null || !chunkIndex.TryGetValue(chunkId, out var chunk))
        {
            return null;
        }

        return new BatchResultDto
        {
            Engine = joinRow.Engine,
            KnowledgeItem = new KnowledgeLinkDto
            {
                Id = chunkId,
                Kind = chunk.Knowledge.Kind,
                Title = chunk.Knowledge.Title,
                Summary = chunk.Knowledge.Summary
            },
            Source = BuildSource(chunk.Knowledge.Source)
        };
    }

    private static string? ResolveChunkId(BatchResultJoinRow joinRow)
    {
        if (!string.IsNullOrEmpty(joinRow.ChunkId))
        {
            return joinRow.ChunkId;
        }

        if (joinRow.DocumentId is not null && joinRow.SourceChunkIndex is not null)
        {
            return ChunkRowBuilder.BuildChunkId(joinRow.DocumentId, joinRow.SourceChunkIndex.Value);
        }

        return null;
    }

    private static SourceDto BuildSource(KnowledgeSource source) => new()
    {
        PageNumber = source.PageNumber,
        FilePath = source.FilePath,
        LineStart = source.LineStart,
        LineEnd = source.LineEnd
    };
}
