using Cortexa.JobOrchestrator.Application.Exceptions;
using Cortexa.JobOrchestrator.Application.Interfaces;
using Cortexa.JobOrchestrator.Application.Models;
using Cortexa.JobOrchestrator.Domain.Entities;

namespace Cortexa.JobOrchestrator.Application.Handlers;

public sealed class GetDocumentHandler
{
    private readonly ISagaRepository _sagas;
    private readonly IViewableDocumentReader _reader;

    public GetDocumentHandler(ISagaRepository sagas, IViewableDocumentReader reader)
    {
        _sagas = sagas;
        _reader = reader;
    }

    public async Task<DocumentContentResult> HandleAsync(
        string batchId,
        string documentId,
        BatchAccess access,
        CancellationToken ct)
    {
        var saga = await _sagas.GetAsync(batchId, ct);
        EnsureAuthorized(saga, batchId, access);

        var lookup = await _reader.GetAsync(batchId, documentId, ct);
        return ToResult(lookup, documentId);
    }

    private static DocumentContentResult ToResult(ViewableDocumentLookupResult lookup, string documentId) =>
        lookup.Status switch
        {
            ViewableDocumentStatus.Found =>
                new DocumentContentResult(lookup.Content!, lookup.ContentType!, lookup.FileName!),
            ViewableDocumentStatus.NoViewableArtifact => throw new ViewableArtifactNotFoundException(documentId),
            _ => throw new DocumentNotFoundException(documentId)
        };

    private static void EnsureAuthorized(BatchSaga? saga, string batchId, BatchAccess access)
    {
        if (access.IsSuperAdmin)
            return;

        if (saga is null)
            throw new BatchNotFoundException(batchId);

        if (IsSameOrg(saga.Metadata?.OwnerOrgId, access.CallerOrgId))
            return;

        throw new CrossOrgAccessException(batchId);
    }

    private static bool IsSameOrg(string? ownerOrgId, string? callerOrgId) =>
        ownerOrgId is not null && string.Equals(ownerOrgId, callerOrgId, StringComparison.Ordinal);
}
