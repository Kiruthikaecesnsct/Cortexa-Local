namespace Cortexa.JobOrchestrator.Application.Exceptions;

public sealed class DocumentNotFoundException(string documentId)
    : Exception($"Document '{documentId}' was not found.");
