using Cortexa.JobOrchestrator.Application.Exceptions;
using Cortexa.JobOrchestrator.Application.Handlers;
using Cortexa.JobOrchestrator.Application.Interfaces;
using Cortexa.JobOrchestrator.Application.Models;
using Cortexa.JobOrchestrator.Domain.Entities;
using Cortexa.JobOrchestrator.Domain.Enums;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace Cortexa.JobOrchestrator.Application.Tests;

public sealed class DeleteBatchHandlerTests
{
    private readonly ISagaRepository _sagas = Substitute.For<ISagaRepository>();
    private readonly ILogger<DeleteBatchHandler> _logger = Substitute.For<ILogger<DeleteBatchHandler>>();

    private const string CallerOrgId = "org-1";

    private static DeleteBatchContext Context(string batchId, bool force = false, BatchAccess? access = null) =>
        new(
            batchId,
            new DeletionRequestOptions("operator-1", "corr-1", force),
            access ?? new BatchAccess(CallerOrgId, IsSuperAdmin: false));

    private static BatchSaga BuildSaga(string batchId, BatchState state, string? ownerOrgId = CallerOrgId) =>
        new(
            batchId,
            state,
            documents: [],
            wantsHarvesting: false,
            wantsSeeding: false,
            version: 1,
            eTag: "\"etag-1\"",
            schemaVersion: 1,
            activeDocumentIds: new HashSet<string>(),
            queuedDocumentIds: new Queue<string>(),
            metadata: new BatchMetadata(OwnerOrgId: ownerOrgId));

    private static IBatchDeleter Deleter(string name, bool success, int count = 1)
    {
        var deleter = Substitute.For<IBatchDeleter>();
        deleter.StoreName.Returns(name);
        deleter
            .DeleteAsync(Arg.Any<string>(), Arg.Any<DeleteBatchContext>(), Arg.Any<CancellationToken>())
            .Returns(new StoreDeletionResult(name, count, success));
        return deleter;
    }

    private DeleteBatchHandler Build(params IBatchDeleter[] deleters) =>
        new(_sagas, deleters, _logger);

    [Theory]
    [InlineData(BatchState.Completed)]
    [InlineData(BatchState.Failed)]
    [InlineData(BatchState.Cancelled)]
    public async Task HandleAsync_TerminalBatch_DeletesAllStoresAndSaga(BatchState state)
    {
        _sagas.GetAsync("batch-1", Arg.Any<CancellationToken>()).Returns(BuildSaga("batch-1", state));
        var handler = Build(Deleter("Cosmos", true, 5), Deleter("BlobStorage", true, 2));

        var result = await handler.HandleAsync(Context("batch-1"), CancellationToken.None);

        result.FullyDeleted.Should().BeTrue();
        result.Status.Should().Be("Deleted");
        result.Stores.Should().Contain(s => s.StoreName == "CosmosBatches" && s.DeletedCount == 1);
        await _sagas.Received(1).DeleteAsync("batch-1", Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(BatchState.Queued)]
    [InlineData(BatchState.InProgress)]
    public async Task HandleAsync_RunningBatchWithoutForce_ThrowsAndSkipsDeleters(BatchState state)
    {
        _sagas.GetAsync("batch-1", Arg.Any<CancellationToken>()).Returns(BuildSaga("batch-1", state));
        var deleter = Deleter("Cosmos", true);
        var handler = Build(deleter);

        var act = async () => await handler.HandleAsync(Context("batch-1"), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidBatchStateException>();
        await deleter.DidNotReceive().DeleteAsync(Arg.Any<string>(), Arg.Any<DeleteBatchContext>(), Arg.Any<CancellationToken>());
        await _sagas.DidNotReceive().DeleteAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_RunningBatchWithForce_DeletesAllStoresAndSaga()
    {
        _sagas.GetAsync("batch-1", Arg.Any<CancellationToken>()).Returns(BuildSaga("batch-1", BatchState.InProgress));
        var handler = Build(Deleter("Cosmos", true));

        var result = await handler.HandleAsync(Context("batch-1", force: true), CancellationToken.None);

        result.FullyDeleted.Should().BeTrue();
        await _sagas.Received(1).DeleteAsync("batch-1", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_SuperAdminMissingSaga_IsIdempotentAndSucceeds()
    {
        _sagas.GetAsync("gone", Arg.Any<CancellationToken>()).Returns((BatchSaga?)null);
        var handler = Build(Deleter("Cosmos", true, 0));

        var result = await handler.HandleAsync(
            Context("gone", access: BatchAccess.Unrestricted),
            CancellationToken.None);

        result.FullyDeleted.Should().BeTrue();
        result.Status.Should().Be("Deleted");
        result.Stores.Should().Contain(s => s.StoreName == "CosmosBatches" && s.DeletedCount == 0);
        await _sagas.Received(1).DeleteAsync("gone", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_AdminMissingSaga_ThrowsBatchNotFoundException()
    {
        _sagas.GetAsync("gone", Arg.Any<CancellationToken>()).Returns((BatchSaga?)null);
        var deleter = Deleter("Cosmos", true, 0);
        var handler = Build(deleter);

        var act = async () => await handler.HandleAsync(Context("gone"), CancellationToken.None);

        await act.Should().ThrowAsync<BatchNotFoundException>();
        await deleter.DidNotReceive().DeleteAsync(Arg.Any<string>(), Arg.Any<DeleteBatchContext>(), Arg.Any<CancellationToken>());
        await _sagas.DidNotReceive().DeleteAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_AdminSameOrg_DeletesAllStoresAndSaga()
    {
        _sagas.GetAsync("batch-1", Arg.Any<CancellationToken>())
            .Returns(BuildSaga("batch-1", BatchState.Completed, ownerOrgId: CallerOrgId));
        var handler = Build(Deleter("Cosmos", true));

        var result = await handler.HandleAsync(Context("batch-1"), CancellationToken.None);

        result.FullyDeleted.Should().BeTrue();
        await _sagas.Received(1).DeleteAsync("batch-1", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_AdminCrossOrg_ThrowsCrossOrgAccessException()
    {
        _sagas.GetAsync("batch-1", Arg.Any<CancellationToken>())
            .Returns(BuildSaga("batch-1", BatchState.Completed, ownerOrgId: "org-other"));
        var deleter = Deleter("Cosmos", true);
        var handler = Build(deleter);

        var act = async () => await handler.HandleAsync(Context("batch-1"), CancellationToken.None);

        await act.Should().ThrowAsync<CrossOrgAccessException>();
        await deleter.DidNotReceive().DeleteAsync(Arg.Any<string>(), Arg.Any<DeleteBatchContext>(), Arg.Any<CancellationToken>());
        await _sagas.DidNotReceive().DeleteAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_AdminLegacyBatchNoOrgId_ThrowsCrossOrgAccessException()
    {
        _sagas.GetAsync("batch-1", Arg.Any<CancellationToken>())
            .Returns(BuildSaga("batch-1", BatchState.Completed, ownerOrgId: null));
        var handler = Build(Deleter("Cosmos", true));

        var act = async () => await handler.HandleAsync(Context("batch-1"), CancellationToken.None);

        await act.Should().ThrowAsync<CrossOrgAccessException>();
    }

    [Fact]
    public async Task HandleAsync_SuperAdminCrossOrg_DeletesAllStoresAndSaga()
    {
        _sagas.GetAsync("batch-1", Arg.Any<CancellationToken>())
            .Returns(BuildSaga("batch-1", BatchState.Completed, ownerOrgId: "org-other"));
        var handler = Build(Deleter("Cosmos", true));

        var result = await handler.HandleAsync(
            Context("batch-1", access: BatchAccess.Unrestricted),
            CancellationToken.None);

        result.FullyDeleted.Should().BeTrue();
        await _sagas.Received(1).DeleteAsync("batch-1", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_SuccessfulDelete_LogsOrgIdAndUserIdForAudit()
    {
        _sagas.GetAsync("batch-1", Arg.Any<CancellationToken>())
            .Returns(BuildSaga("batch-1", BatchState.Completed, ownerOrgId: CallerOrgId));
        var handler = Build(Deleter("Cosmos", true));

        await handler.HandleAsync(Context("batch-1"), CancellationToken.None);

        _logger.Received(1).Log(
            LogLevel.Information,
            Arg.Any<EventId>(),
            Arg.Is<object>(state => state.ToString()!.Contains($"org_id={CallerOrgId}") && state.ToString()!.Contains("user_id=operator-1")),
            null,
            Arg.Any<Func<object, Exception?, string>>());
    }

    [Fact]
    public async Task HandleAsync_CrossOrgDenied_LogsDeniedAuditLine()
    {
        _sagas.GetAsync("batch-1", Arg.Any<CancellationToken>())
            .Returns(BuildSaga("batch-1", BatchState.Completed, ownerOrgId: "org-other"));
        var handler = Build(Deleter("Cosmos", true));

        var act = async () => await handler.HandleAsync(Context("batch-1"), CancellationToken.None);
        await act.Should().ThrowAsync<CrossOrgAccessException>();

        _logger.Received(1).Log(
            LogLevel.Warning,
            Arg.Any<EventId>(),
            Arg.Is<object>(state => state.ToString()!.Contains("delete_denied") && state.ToString()!.Contains("cross_org")),
            null,
            Arg.Any<Func<object, Exception?, string>>());
    }

    [Fact]
    public async Task HandleAsync_StoreFailure_LeavesSagaIntactAndReportsPartial()
    {
        _sagas.GetAsync("batch-1", Arg.Any<CancellationToken>()).Returns(BuildSaga("batch-1", BatchState.Failed));
        var failing = Deleter("BlobStorage", false);
        var succeeding = Deleter("Cosmos", true);
        var handler = Build(succeeding, failing);

        var result = await handler.HandleAsync(Context("batch-1"), CancellationToken.None);

        result.FullyDeleted.Should().BeFalse();
        result.Status.Should().Be("PartiallyDeleted");
        await _sagas.DidNotReceive().DeleteAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await succeeding.Received(1).DeleteAsync("batch-1", Arg.Any<DeleteBatchContext>(), Arg.Any<CancellationToken>());
        await failing.Received(1).DeleteAsync("batch-1", Arg.Any<DeleteBatchContext>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_SagaDeleteThrows_ReportsPartialFailure()
    {
        _sagas.GetAsync("batch-1", Arg.Any<CancellationToken>()).Returns(BuildSaga("batch-1", BatchState.Completed));
        _sagas.DeleteAsync("batch-1", Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new InvalidOperationException("cosmos down")));
        var handler = Build(Deleter("Cosmos", true));

        var result = await handler.HandleAsync(Context("batch-1"), CancellationToken.None);

        result.FullyDeleted.Should().BeFalse();
        result.Status.Should().Be("PartiallyDeleted");
        result.Stores.Should().Contain(s => s.StoreName == "CosmosBatches" && !s.Success);
    }

    [Fact]
    public async Task HandleAsync_AggregatesPerStoreCountsInResponse()
    {
        _sagas.GetAsync("batch-1", Arg.Any<CancellationToken>()).Returns(BuildSaga("batch-1", BatchState.Completed));
        var handler = Build(Deleter("Cosmos", true, 7), Deleter("KeyVault", true, 1));

        var result = await handler.HandleAsync(Context("batch-1"), CancellationToken.None);

        result.Stores.Should().Contain(s => s.StoreName == "Cosmos" && s.DeletedCount == 7);
        result.Stores.Should().Contain(s => s.StoreName == "KeyVault" && s.DeletedCount == 1);
    }

    [Fact]
    public async Task HandleAsync_SagaDeleteSucceeds_AfterRetryInsideRepository()
    {
        _sagas.GetAsync("batch-1", Arg.Any<CancellationToken>()).Returns(BuildSaga("batch-1", BatchState.Completed));
        _sagas.DeleteAsync("batch-1", Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        var handler = Build(Deleter("Cosmos", true));

        var result = await handler.HandleAsync(Context("batch-1"), CancellationToken.None);

        result.FullyDeleted.Should().BeTrue();
        result.Stores.Should().Contain(s => s.StoreName == "CosmosBatches" && s.Success);
        await _sagas.Received(1).DeleteAsync("batch-1", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_PersistentSagaDeleteFailure_SetsFullyDeletedFalseAndPopulatesError()
    {
        _sagas.GetAsync("batch-1", Arg.Any<CancellationToken>()).Returns(BuildSaga("batch-1", BatchState.Completed));
        _sagas.DeleteAsync("batch-1", Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new InvalidOperationException("persistent error")));
        var handler = Build(Deleter("Cosmos", true));

        var result = await handler.HandleAsync(Context("batch-1"), CancellationToken.None);

        result.FullyDeleted.Should().BeFalse();
        result.Status.Should().Be("PartiallyDeleted");
        result.Stores.Should().Contain(s => s.StoreName == "CosmosBatches" && !s.Success);
        result.Stores.Should().Contain(s => s.StoreName == "CosmosBatches" && s.Error == "persistent error");
    }

    [Fact]
    public async Task LogAudit_StoreWithError_IncludesErrorInAuditLog()
    {
        _sagas.GetAsync("batch-1", Arg.Any<CancellationToken>()).Returns(BuildSaga("batch-1", BatchState.Completed));
        var failingDeleter = Substitute.For<IBatchDeleter>();
        failingDeleter.StoreName.Returns("BlobStorage");
        failingDeleter
            .DeleteAsync(Arg.Any<string>(), Arg.Any<DeleteBatchContext>(), Arg.Any<CancellationToken>())
            .Returns(new StoreDeletionResult("BlobStorage", 0, false, "connection timeout"));
        var handler = Build(failingDeleter);

        await handler.HandleAsync(Context("batch-1"), CancellationToken.None);

        _logger.Received().Log(
            LogLevel.Information,
            Arg.Any<EventId>(),
            Arg.Is<object>(state => state.ToString()!.Contains("BlobStorage:0:fail:connection timeout")),
            null,
            Arg.Any<Func<object, Exception?, string>>());
    }

    [Fact]
    public async Task LogAudit_SagaDeleteFails_LogsBatchDeleteStoreFailed()
    {
        _sagas.GetAsync("batch-1", Arg.Any<CancellationToken>()).Returns(BuildSaga("batch-1", BatchState.Completed));
        _sagas.DeleteAsync("batch-1", Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new InvalidOperationException("cosmos down")));
        var handler = Build(Deleter("Cosmos", true));

        await handler.HandleAsync(Context("batch-1"), CancellationToken.None);

        _logger.Received().Log(
            LogLevel.Error,
            Arg.Any<EventId>(),
            Arg.Is<object>(state =>
                state.ToString()!.Contains("Batch delete store failed") &&
                state.ToString()!.Contains("store=CosmosBatches") &&
                state.ToString()!.Contains("cosmos down")),
            null,
            Arg.Any<Func<object, Exception?, string>>());
    }

    [Fact]
    public async Task RunDeletersAsync_MultipleDeleters_RunsConcurrently()
    {
        var timestamps = new System.Collections.Concurrent.ConcurrentBag<(string Store, DateTime Entry, DateTime Exit)>();

        var deleter1 = Substitute.For<IBatchDeleter>();
        deleter1.StoreName.Returns("Store1");
        deleter1.DeleteAsync(Arg.Any<string>(), Arg.Any<DeleteBatchContext>(), Arg.Any<CancellationToken>())
            .Returns(async _ =>
            {
                var entry = DateTime.UtcNow;
                await Task.Delay(50);
                var exit = DateTime.UtcNow;
                timestamps.Add(("Store1", entry, exit));
                return new StoreDeletionResult("Store1", 1, true);
            });

        var deleter2 = Substitute.For<IBatchDeleter>();
        deleter2.StoreName.Returns("Store2");
        deleter2.DeleteAsync(Arg.Any<string>(), Arg.Any<DeleteBatchContext>(), Arg.Any<CancellationToken>())
            .Returns(async _ =>
            {
                var entry = DateTime.UtcNow;
                await Task.Delay(50);
                var exit = DateTime.UtcNow;
                timestamps.Add(("Store2", entry, exit));
                return new StoreDeletionResult("Store2", 1, true);
            });

        _sagas.GetAsync("batch-1", Arg.Any<CancellationToken>()).Returns(BuildSaga("batch-1", BatchState.Completed));
        var handler = Build(deleter1, deleter2);

        await handler.HandleAsync(Context("batch-1"), CancellationToken.None);

        timestamps.Should().HaveCount(2);
        var store1 = timestamps.First(t => t.Store == "Store1");
        var store2 = timestamps.First(t => t.Store == "Store2");

        var overlapExists = (store1.Entry < store2.Exit && store1.Exit > store2.Entry) ||
                           (store2.Entry < store1.Exit && store2.Exit > store1.Entry);
        overlapExists.Should().BeTrue("deleters should run concurrently with overlapping time windows");
    }
}
