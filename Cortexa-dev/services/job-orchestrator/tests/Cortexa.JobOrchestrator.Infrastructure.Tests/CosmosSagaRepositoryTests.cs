using Cortexa.JobOrchestrator.Application.Interfaces;
using Cortexa.JobOrchestrator.Domain.Entities;
using Cortexa.JobOrchestrator.Domain.Enums;
using Cortexa.JobOrchestrator.Infrastructure.Configuration;
using Cortexa.JobOrchestrator.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Cortexa.JobOrchestrator.Infrastructure.Tests;

public sealed class CosmosSagaRepositoryTests
{
    private const string DatabaseName = "cortexa-pipeline";
    private const string BatchesContainerName = "batches";

    private static CosmosSagaRepository BuildRepository(out Container container)
    {
        container = Substitute.For<Container>();
        var client = Substitute.For<CosmosClient>();
        client.GetContainer(DatabaseName, BatchesContainerName).Returns(container);

        var settings = Options.Create(new CosmosSettings
        {
            Database = DatabaseName,
            BatchesContainer = BatchesContainerName
        });

        var retryPolicy = Substitute.For<IRetryPolicy>();
        retryPolicy.MaxRetries.Returns(3);
        retryPolicy.ShouldRetry(Arg.Any<int>()).Returns(x => (int)x[0] <= 3);
        retryPolicy.DelayFor(Arg.Any<int>()).Returns(TimeSpan.FromMilliseconds(10));

        return new CosmosSagaRepository(client, settings, retryPolicy);
    }

    [Fact]
    public async Task GetAsync_NullBatchId_ReturnsNullWithoutCallingCosmos()
    {
        var repository = BuildRepository(out var container);

        var result = await repository.GetAsync(null!, CancellationToken.None);

        result.Should().BeNull();
        await container.DidNotReceiveWithAnyArgs().ReadItemAsync<SagaDocument>(default!, default);
    }

    [Fact]
    public async Task GetAsync_WhitespaceBatchId_ReturnsNullWithoutCallingCosmos()
    {
        var repository = BuildRepository(out var container);

        var result = await repository.GetAsync("   ", CancellationToken.None);

        result.Should().BeNull();
        await container.DidNotReceiveWithAnyArgs().ReadItemAsync<SagaDocument>(default!, default);
    }

    [Fact]
    public async Task RoundTrip_PreservesCandidateTrackingFields()
    {
        var repository = BuildRepository(out var container);

        var doc1 = new DocumentProgress("doc-001", DocumentState.Extracted);
        doc1.SetExpectedCandidates(8);
        doc1.MarkCandidateScored("cand-001");
        doc1.MarkCandidateScored("cand-002");
        doc1.MarkCandidateScored("cand-003");
        doc1.MarkCandidateScored("cand-004");
        doc1.MarkCandidateScored("cand-005");
        doc1.MarkCandidateScored("cand-006");
        doc1.MarkCandidateFailed("cand-007");
        doc1.MarkCandidateFailed("cand-008");

        var originalSaga = new BatchSaga(
            "batch-123",
            BatchState.InProgress,
            [doc1],
            wantsHarvesting: true,
            wantsSeeding: false,
            version: 1,
            eTag: null,
            schemaVersion: 1);

        SagaDocument? capturedDocument = null;
        container.CreateItemAsync(
            Arg.Do<SagaDocument>(d => capturedDocument = d),
            Arg.Any<PartitionKey>(),
            cancellationToken: Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Substitute.For<ItemResponse<SagaDocument>>()));

        container.ReadItemAsync<SagaDocument>(
            Arg.Any<string>(),
            Arg.Any<PartitionKey>(),
            cancellationToken: Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                var response = Substitute.For<ItemResponse<SagaDocument>>();
                response.Resource.Returns(capturedDocument!);
                response.ETag.Returns("etag-123");
                return response;
            });

        await repository.CreateAsync(originalSaga, CancellationToken.None);
        var reloadedSaga = await repository.GetAsync("batch-123", CancellationToken.None);

        reloadedSaga.Should().NotBeNull();
        var reloadedDoc = reloadedSaga!.Documents.Single();
        reloadedDoc.DocumentId.Should().Be("doc-001");
        reloadedDoc.ExpectedCandidateCount.Should().Be(8);
        reloadedDoc.CompletedCandidateIds.Should().BeEquivalentTo(["cand-001", "cand-002", "cand-003", "cand-004", "cand-005", "cand-006"]);
        reloadedDoc.FailedCandidateIds.Should().BeEquivalentTo(["cand-007", "cand-008"]);
        reloadedDoc.AllCandidatesResolved.Should().BeTrue();
    }

    [Fact]
    public async Task DeleteAsync_TransientCosmosException_RetriesAndSucceeds()
    {
        var repository = BuildRepository(out var container);
        var attempt = 0;

        container.DeleteItemAsync<SagaDocument>(
            Arg.Any<string>(),
            Arg.Any<PartitionKey>(),
            cancellationToken: Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                attempt++;
                if (attempt <= 2)
                    throw new CosmosException("transient", System.Net.HttpStatusCode.TooManyRequests, 0, "act-1", 0);
                return Task.FromResult(Substitute.For<ItemResponse<SagaDocument>>());
            });

        await repository.DeleteAsync("batch-123", CancellationToken.None);

        attempt.Should().Be(3);
    }

    [Fact]
    public async Task DeleteAsync_NotFoundCosmosException_SucceedsIdempotently()
    {
        var repository = BuildRepository(out var container);

        container
            .When(c => c.DeleteItemAsync<SagaDocument>(
                Arg.Any<string>(),
                Arg.Any<PartitionKey>(),
                cancellationToken: Arg.Any<CancellationToken>()))
            .Do(_ => throw new CosmosException("not found", System.Net.HttpStatusCode.NotFound, 0, "act-1", 0));

        await repository.DeleteAsync("batch-123", CancellationToken.None);

        await container.Received(1).DeleteItemAsync<SagaDocument>(
            "batch-123",
            Arg.Any<PartitionKey>(),
            cancellationToken: Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DeleteAsync_PersistentFailure_ThrowsAfterExhaustedRetries()
    {
        var repository = BuildRepository(out var container);
        var attempt = 0;

        container
            .When(c => c.DeleteItemAsync<SagaDocument>(
                Arg.Any<string>(),
                Arg.Any<PartitionKey>(),
                cancellationToken: Arg.Any<CancellationToken>()))
            .Do(_ =>
            {
                attempt++;
                throw new CosmosException("persistent", System.Net.HttpStatusCode.Forbidden, 0, "act-1", 0);
            });

        var act = async () => await repository.DeleteAsync("batch-123", CancellationToken.None);

        await act.Should().ThrowAsync<CosmosException>()
            .WithMessage("*persistent*");
        attempt.Should().Be(1);
    }
}
