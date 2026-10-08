using Collector.Server.Application.Building;
using Collector.Server.Application.Errors;
using Collector.Server.Application.Ports;
using Collector.Server.Application.Rows;
using Microsoft.Extensions.Logging;

namespace Collector.Server.Application.Handlers;

public sealed class IngestionEventDispatcher(
    IIngestionEventPublisher publisher,
    IClock clock,
    ILogger<IngestionEventDispatcher> logger)
{
    private const string PublishStage = "publish";

    public async Task PublishAsync(
        string batchId,
        IReadOnlyList<DocumentRow> documents,
        CancellationToken cancellationToken)
    {
        var correlationId = Guid.NewGuid().ToString();
        var now = clock.UtcNow;
        var published = 0;

        try
        {
            foreach (var document in documents)
            {
                var envelope = IngestionCompletedEventBuilder.Build(document, correlationId, now);
                await publisher.PublishAsync(envelope, cancellationToken);
                published++;
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(
                "Publish failed for batch {BatchId} after {Published} of {Total} events.",
                batchId,
                published,
                documents.Count);
            throw new PipelineWriteException(batchId, PublishStage, exception);
        }
    }
}
