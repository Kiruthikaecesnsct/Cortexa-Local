namespace Collector.Server.Application.Errors;

public sealed class SagaAlreadyExistsException : Exception
{
    public SagaAlreadyExistsException(string batchId)
        : base($"A saga already exists for batch {batchId}.")
    {
        BatchId = batchId;
    }

    public string BatchId { get; }
}
