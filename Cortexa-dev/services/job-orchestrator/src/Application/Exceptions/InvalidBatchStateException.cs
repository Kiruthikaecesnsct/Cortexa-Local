namespace Cortexa.JobOrchestrator.Application.Exceptions;

public sealed class InvalidBatchStateException(string batchId)
    : Exception($"Cannot delete a Running batch; use force flag or cancel first (BUG054). Batch '{batchId}'.");
