namespace Cortexa.JobOrchestrator.Application.Models;

public enum ViewableDocumentStatus
{
    Found,
    DocumentNotFound,
    NoViewableArtifact
}

public sealed record ViewableDocumentLookupResult(
    ViewableDocumentStatus Status,
    Stream? Content = null,
    string? ContentType = null,
    string? FileName = null)
{
    public static ViewableDocumentLookupResult Found(Stream content, string contentType, string fileName) =>
        new(ViewableDocumentStatus.Found, content, contentType, fileName);

    public static readonly ViewableDocumentLookupResult NotFound = new(ViewableDocumentStatus.DocumentNotFound);

    public static readonly ViewableDocumentLookupResult NoArtifact = new(ViewableDocumentStatus.NoViewableArtifact);
}

public sealed record DocumentContentResult(Stream Content, string ContentType, string FileName);
