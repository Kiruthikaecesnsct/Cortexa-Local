namespace Cortexa.JobOrchestrator.Application.Exceptions;

public sealed class CrossOrgAccessException(string batchId)
    : Exception($"Batch '{batchId}' does not belong to the caller's organization.");
