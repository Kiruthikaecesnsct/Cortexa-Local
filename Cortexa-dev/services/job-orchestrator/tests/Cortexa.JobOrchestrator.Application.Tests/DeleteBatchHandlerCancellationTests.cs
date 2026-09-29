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

public sealed class DeleteBatchHandlerCancellationTests
{
    private readonly ISagaRepository _sagas = Substitute.For<ISagaRepository>();
    private readonly ILogger<DeleteBatchHandler> _logger = Substitute.For<ILogger<DeleteBatchHandler>>();

    private const string BatchId = "batch-1";
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

    private static IBatchDeleter Deleter(string name, bool success, int count = 1, TimeSpan? delay = null)
    {
        var deleter = Substitute.For<IBatchDeleter>();
        deleter.StoreName.Returns(name);
        deleter
            .DeleteAsync(Arg.Any<string>(), Arg.Any<DeleteBatchContext>(), Arg.Any<CancellationToken>())
            .Returns(async _ =>
            {
                if (delay.HasValue)
                    await Task.Delay(delay.Value);
                return new StoreDeletionResult(name, count, success);
            });
        return deleter;
    }

    private DeleteBatchHandler Build(params IBatchDeleter[] deleters) =>
        new(_sagas, deleters, _logger);

    [Fact]
    public async Task HandleAsync_RequestTokenCancelledBeforeSagaDelete_SagaDeleteStillRuns()
    {
        var requestCts = new CancellationTokenSource();

        _sagas.GetAsync(BatchId, Arg.Any<CancellationToken>())
            .Returns(BuildSaga(BatchId, BatchState.Completed));

        var sagaDeleteInvoked = false;
        _sagas.DeleteAsync(BatchId, Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                sagaDeleteInvoked = true;
                return Task.CompletedTask;
            });

        var deleter = Substitute.For<IBatchDeleter>();
        deleter.StoreName.Returns("Store1");
        deleter.DeleteAsync(Arg.Any<string>(), Arg.Any<DeleteBatchContext>(), Arg.Any<CancellationToken>())
            .Returns(async _ =>
            {
                requestCts.Cancel();
                return new StoreDeletionResult("Store1", 5, true);
            });

        var handler = Build(deleter);

        var result = await handler.HandleAsync(Context(BatchId), requestCts.Token);

        result.FullyDeleted.Should().BeTrue();
        sagaDeleteInvoked.Should().BeTrue();
        result.Stores.Should().Contain(s => s.StoreName == "CosmosBatches" && s.Success);
    }

    [Fact]
    public async Task HandleAsync_AllDeletersSucceedWithCancelledToken_YieldsFullyDeletedTrue()
    {
        var requestCts = new CancellationTokenSource();
        requestCts.Cancel();

        _sagas.GetAsync(BatchId, Arg.Any<CancellationToken>())
            .Returns(BuildSaga(BatchId, BatchState.Completed));

        _sagas.DeleteAsync(BatchId, Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var handler = Build(Deleter("Cosmos", true, 3), Deleter("BlobStorage", true, 1));

        var result = await handler.HandleAsync(Context(BatchId), requestCts.Token);

        result.FullyDeleted.Should().BeTrue();
        result.Status.Should().Be("Deleted");
        await _sagas.Received(1).DeleteAsync(BatchId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_SagaLastOrdering_DataStoresDeletedBeforeSaga()
    {
        var deleteOrder = new List<string>();

        _sagas.GetAsync(BatchId, Arg.Any<CancellationToken>())
            .Returns(BuildSaga(BatchId, BatchState.Completed));

        _sagas.DeleteAsync(BatchId, Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                deleteOrder.Add("CosmosBatches");
                return Task.CompletedTask;
            });

        var deleter1 = Substitute.For<IBatchDeleter>();
        deleter1.StoreName.Returns("Store1");
        deleter1.DeleteAsync(Arg.Any<string>(), Arg.Any<DeleteBatchContext>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                deleteOrder.Add("Store1");
                return Task.FromResult(new StoreDeletionResult("Store1", 1, true));
            });

        var deleter2 = Substitute.For<IBatchDeleter>();
        deleter2.StoreName.Returns("Store2");
        deleter2.DeleteAsync(Arg.Any<string>(), Arg.Any<DeleteBatchContext>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                deleteOrder.Add("Store2");
                return Task.FromResult(new StoreDeletionResult("Store2", 1, true));
            });

        var handler = Build(deleter1, deleter2);

        await handler.HandleAsync(Context(BatchId), CancellationToken.None);

        deleteOrder.Should().HaveCount(3);
        deleteOrder.Should().Contain("Store1");
        deleteOrder.Should().Contain("Store2");
        deleteOrder.Should().Contain("CosmosBatches");

        deleteOrder.IndexOf("CosmosBatches").Should().Be(2);
    }

    [Fact]
    public async Task HandleAsync_OneDeleterFails_SagaNotDeleted()
    {
        _sagas.GetAsync(BatchId, Arg.Any<CancellationToken>())
            .Returns(BuildSaga(BatchId, BatchState.Completed));

        var handler = Build(Deleter("Store1", true), Deleter("Store2", false));

        var result = await handler.HandleAsync(Context(BatchId), CancellationToken.None);

        result.FullyDeleted.Should().BeFalse();
        await _sagas.DidNotReceive().DeleteAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_LogAuditCalled_EvenWithRequestTokenCancelled()
    {
        var requestCts = new CancellationTokenSource();
        requestCts.Cancel();

        _sagas.GetAsync(BatchId, Arg.Any<CancellationToken>())
            .Returns(BuildSaga(BatchId, BatchState.Completed));

        _sagas.DeleteAsync(BatchId, Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var handler = Build(Deleter("Cosmos", true));

        await handler.HandleAsync(Context(BatchId), requestCts.Token);

        _logger.Received(1).Log(
            LogLevel.Information,
            Arg.Any<EventId>(),
            Arg.Is<object>(state =>
                state.ToString()!.Contains("Batch delete cascade") &&
                state.ToString()!.Contains("fully_deleted=True")),
            null,
            Arg.Any<Func<object, Exception?, string>>());
    }

    [Fact]
    public async Task HandleAsync_SagaDeleteThrowsWithCancelledToken_LogsBatchDeleteStoreFailed()
    {
        var requestCts = new CancellationTokenSource();

        _sagas.GetAsync(BatchId, Arg.Any<CancellationToken>())
            .Returns(BuildSaga(BatchId, BatchState.Completed));

        _sagas.DeleteAsync(BatchId, Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new InvalidOperationException("cosmos down")));

        var handler = Build(Deleter("Cosmos", true));

        var result = await handler.HandleAsync(Context(BatchId), requestCts.Token);

        result.FullyDeleted.Should().BeFalse();

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
    public async Task HandleAsync_DataDeletersRunConcurrently_SagaDeleteRunsAfter()
    {
        var timestamps = new System.Collections.Concurrent.ConcurrentBag<(string Store, DateTime Entry, DateTime Exit)>();
        var sagaDeleteTimestamp = DateTime.MinValue;

        _sagas.GetAsync(BatchId, Arg.Any<CancellationToken>())
            .Returns(BuildSaga(BatchId, BatchState.Completed));

        _sagas.DeleteAsync(BatchId, Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                sagaDeleteTimestamp = DateTime.UtcNow;
                return Task.CompletedTask;
            });

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

        var handler = Build(deleter1, deleter2);

        await handler.HandleAsync(Context(BatchId), CancellationToken.None);

        timestamps.Should().HaveCount(2);
        var store1 = timestamps.First(t => t.Store == "Store1");
        var store2 = timestamps.First(t => t.Store == "Store2");

        var allDeletersFinished = new[] { store1.Exit, store2.Exit }.Max();
        sagaDeleteTimestamp.Should().BeAfter(allDeletersFinished);
    }

    [Fact]
    public async Task HandleAsync_SlowDeletersWithCancelledToken_SagaDeleteStillWaitsForAllDeleters()
    {
        var requestCts = new CancellationTokenSource();
        var allDeletersCompleted = false;

        _sagas.GetAsync(BatchId, Arg.Any<CancellationToken>())
            .Returns(BuildSaga(BatchId, BatchState.Completed));

        _sagas.DeleteAsync(BatchId, Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                allDeletersCompleted.Should().BeTrue();
                return Task.CompletedTask;
            });

        var deleter1 = Deleter("Store1", true, 1, TimeSpan.FromMilliseconds(50));
        var deleter2 = Substitute.For<IBatchDeleter>();
        deleter2.StoreName.Returns("Store2");
        deleter2.DeleteAsync(Arg.Any<string>(), Arg.Any<DeleteBatchContext>(), Arg.Any<CancellationToken>())
            .Returns(async _ =>
            {
                requestCts.Cancel();
                await Task.Delay(80);
                allDeletersCompleted = true;
                return new StoreDeletionResult("Store2", 1, true);
            });

        var handler = Build(deleter1, deleter2);

        var result = await handler.HandleAsync(Context(BatchId), requestCts.Token);

        result.FullyDeleted.Should().BeTrue();
        allDeletersCompleted.Should().BeTrue();
    }
}
