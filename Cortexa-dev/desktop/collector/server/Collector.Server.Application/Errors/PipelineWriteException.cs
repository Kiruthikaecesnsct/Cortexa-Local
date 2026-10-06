namespace Collector.Server.Application.Errors;

public sealed class PipelineWriteException : Exception
{
    public PipelineWriteException(string batchId, string stage, Exception innerException)
        : base($"Pipeline write failed for batch {batchId} at stage {stage}.", innerException)
    {
        BatchId = batchId;
        Stage = stage;
    }

    public string BatchId { get; }

    public string Stage { get; }
}
