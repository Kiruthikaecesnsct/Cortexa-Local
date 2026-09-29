namespace Cortexa.JobOrchestrator.Application.Exceptions;

public sealed class BatchNotFoundException(string batchId)
    : Exception($"Batch '{batchId}' was not found.");
