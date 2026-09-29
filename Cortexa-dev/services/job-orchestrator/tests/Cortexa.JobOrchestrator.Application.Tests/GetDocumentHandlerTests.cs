using Cortexa.JobOrchestrator.Application.Exceptions;
using Cortexa.JobOrchestrator.Application.Handlers;
using Cortexa.JobOrchestrator.Application.Interfaces;
using Cortexa.JobOrchestrator.Application.Models;
using Cortexa.JobOrchestrator.Domain.Entities;
using Cortexa.JobOrchestrator.Domain.Enums;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Cortexa.JobOrchestrator.Application.Tests;

public sealed class GetDocumentHandlerTests
{
    private const string CallerOrgId = "org-1";

    private readonly ISagaRepository _sagas = Substitute.For<ISagaRepository>();
    private readonly IViewableDocumentReader _reader = Substitute.For<IViewableDocumentReader>();

    private GetDocumentHandler Build() => new(_sagas, _reader);

    private static BatchSaga BuildSaga(string batchId, string? ownerOrgId = CallerOrgId) =>
        new(
            batchId,
            BatchState.Completed,
            documents: [],
            wantsHarvesting: true,
            wantsSeeding: false,
            version: 1,
            eTag: "\"etag-1\"",
            schemaVersion: 1,
            activeDocumentIds: new HashSet<string>(),
            queuedDocumentIds: new Queue<string>(),
            metadata: new BatchMetadata(OwnerOrgId: ownerOrgId));

    [Fact]
    public async Task HandleAsync_AuthorizedSameOrg_ReturnsStream()
    {
        _sagas.GetAsync("batch-1", Arg.Any<CancellationToken>()).Returns(BuildSaga("batch-1"));
        using var content = new MemoryStream([1, 2, 3]);
        _reader.GetAsync("batch-1", "doc-1", Arg.Any<CancellationToken>())
            .Returns(ViewableDocumentLookupResult.Found(content, "application/pdf", "doc-1.pdf"));

        var handler = Build();
        var result = await handler.HandleAsync(
            "batch-1", "doc-1", new BatchAccess(CallerOrgId, IsSuperAdmin: false), CancellationToken.None);

        result.ContentType.Should().Be("application/pdf");
        result.FileName.Should().Be("doc-1.pdf");
        result.Content.Should().BeSameAs(content);
    }

    [Fact]
    public async Task HandleAsync_SuperAdminBypassesOrgCheck_ReturnsStream()
    {
        _sagas.GetAsync("batch-1", Arg.Any<CancellationToken>()).Returns(BuildSaga("batch-1", ownerOrgId: "org-other"));
        using var content = new MemoryStream([1]);
        _reader.GetAsync("batch-1", "doc-1", Arg.Any<CancellationToken>())
            .Returns(ViewableDocumentLookupResult.Found(content, "application/pdf", "doc-1.pdf"));

        var handler = Build();
        var result = await handler.HandleAsync(
            "batch-1", "doc-1", BatchAccess.Unrestricted, CancellationToken.None);

        result.Content.Should().BeSameAs(content);
    }

    [Fact]
    public async Task HandleAsync_CrossOrgCaller_ThrowsCrossOrgAccessException()
    {
        _sagas.GetAsync("batch-1", Arg.Any<CancellationToken>()).Returns(BuildSaga("batch-1", ownerOrgId: "org-other"));
        var handler = Build();

        var act = async () => await handler.HandleAsync(
            "batch-1", "doc-1", new BatchAccess(CallerOrgId, IsSuperAdmin: false), CancellationToken.None);

        await act.Should().ThrowAsync<CrossOrgAccessException>();
        await _reader.DidNotReceive().GetAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_LegacyBatchNoOwnerOrgId_ThrowsCrossOrgAccessException()
    {
        _sagas.GetAsync("batch-1", Arg.Any<CancellationToken>()).Returns(BuildSaga("batch-1", ownerOrgId: null));
        var handler = Build();

        var act = async () => await handler.HandleAsync(
            "batch-1", "doc-1", new BatchAccess(CallerOrgId, IsSuperAdmin: false), CancellationToken.None);

        await act.Should().ThrowAsync<CrossOrgAccessException>();
    }

    [Fact]
    public async Task HandleAsync_UnknownBatch_ThrowsBatchNotFoundException()
    {
        _sagas.GetAsync("gone", Arg.Any<CancellationToken>()).Returns((BatchSaga?)null);
        var handler = Build();

        var act = async () => await handler.HandleAsync(
            "gone", "doc-1", new BatchAccess(CallerOrgId, IsSuperAdmin: false), CancellationToken.None);

        await act.Should().ThrowAsync<BatchNotFoundException>();
        await _reader.DidNotReceive().GetAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_SuperAdminUnknownBatch_BypassesBatchNotFoundAndConsultsReader()
    {
        _sagas.GetAsync("gone", Arg.Any<CancellationToken>()).Returns((BatchSaga?)null);
        _reader.GetAsync("gone", "doc-1", Arg.Any<CancellationToken>())
            .Returns(ViewableDocumentLookupResult.NotFound);

        var handler = Build();

        var act = async () => await handler.HandleAsync(
            "gone", "doc-1", BatchAccess.Unrestricted, CancellationToken.None);

        await act.Should().ThrowAsync<DocumentNotFoundException>();
    }

    [Fact]
    public async Task HandleAsync_UnknownDocument_ThrowsDocumentNotFoundException()
    {
        _sagas.GetAsync("batch-1", Arg.Any<CancellationToken>()).Returns(BuildSaga("batch-1"));
        _reader.GetAsync("batch-1", "missing", Arg.Any<CancellationToken>())
            .Returns(ViewableDocumentLookupResult.NotFound);

        var handler = Build();

        var act = async () => await handler.HandleAsync(
            "batch-1", "missing", new BatchAccess(CallerOrgId, IsSuperAdmin: false), CancellationToken.None);

        await act.Should().ThrowAsync<DocumentNotFoundException>();
    }

    [Fact]
    public async Task HandleAsync_DocumentWithoutViewableArtifact_ThrowsViewableArtifactNotFoundException()
    {
        _sagas.GetAsync("batch-1", Arg.Any<CancellationToken>()).Returns(BuildSaga("batch-1"));
        _reader.GetAsync("batch-1", "doc-legacy", Arg.Any<CancellationToken>())
            .Returns(ViewableDocumentLookupResult.NoArtifact);

        var handler = Build();

        var act = async () => await handler.HandleAsync(
            "batch-1", "doc-legacy", new BatchAccess(CallerOrgId, IsSuperAdmin: false), CancellationToken.None);

        await act.Should().ThrowAsync<ViewableArtifactNotFoundException>();
    }
}
