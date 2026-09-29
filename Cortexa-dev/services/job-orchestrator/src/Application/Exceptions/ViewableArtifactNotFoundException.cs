namespace Cortexa.JobOrchestrator.Application.Exceptions;

public sealed class ViewableArtifactNotFoundException(string documentId)
    : Exception($"Document '{documentId}' has no viewable artifact.");
