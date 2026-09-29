using System.Net;
using Cortexa.JobOrchestrator.Application.Interfaces;
using Cortexa.JobOrchestrator.Infrastructure.Configuration;
using Cortexa.JobOrchestrator.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Cortexa.JobOrchestrator.Infrastructure.Tests;

public sealed class CosmosSagaRepositoryDeleteAsyncTests
{
    private readonly CosmosClient _client = Substitute.For<CosmosClient>();
    private readonly Container _container = Substitute.For<Container>();
    private readonly IRetryPolicy _retryPolicy = Substitute.For<IRetryPolicy>();
    private readonly CosmosSettings _settings;

    private const string BatchId = "batch-123";
    private const int SagaDeleteDeadlineSeconds = 15;

    public CosmosSagaRepositoryDeleteAsyncTests()
    {
        _settings = new CosmosSettings
        {
            Database = "cortexa-pipeline",
            BatchesContainer = "batches",
            SagaDeleteDeadlineSeconds = SagaDeleteDeadlineSeconds
        };

        _client.GetContainer(_settings.Database, _settings.BatchesContainer).Returns(_container);

        _retryPolicy.MaxRetries.Returns(3);
        _retryPolicy.ShouldRetry(Arg.Any<int>()).Returns(x => (int)x[0] <= 3);
        _retryPolicy.DelayFor(Arg.Any<int>()).Returns(TimeSpan.FromMilliseconds(5));
    }

    private CosmosSagaRepository BuildRepository() =>
        new(_client, Options.Create(_settings), _retryPolicy);

    [Fact]
    public async Task DeleteAsync_RequestTokenAlreadyCancelled_CompletesSuccessfully()
    {
        var requestCts = new CancellationTokenSource();
        requestCts.Cancel();

        _container.DeleteItemAsync<SagaDocument>(
            BatchId,
            Arg.Any<PartitionKey>(),
            cancellationToken: Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Substitute.For<ItemResponse<SagaDocument>>()));

        var repository = BuildRepository();

        var act = async () => await repository.DeleteAsync(BatchId, requestCts.Token);

        await act.Should().NotThrowAsync();
        await _container.Received(1).DeleteItemAsync<SagaDocument>(
            BatchId,
            Arg.Any<PartitionKey>(),
            cancellationToken: Arg.Is<CancellationToken>(ct => !ct.IsCancellationRequested));
    }

    [Fact]
    public async Task DeleteAsync_ServerOwnedDeadlineRespected_TokenCancelledAfterDeadline()
    {
        var requestCts = new CancellationTokenSource();
        CancellationToken? capturedToken = null;

        _container.DeleteItemAsync<SagaDocument>(
            BatchId,
            Arg.Any<PartitionKey>(),
            cancellationToken: Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                capturedToken = callInfo.Arg<CancellationToken>();
                var response = Substitute.For<ItemResponse<SagaDocument>>();
                return Task.FromResult(response);
            });

        var repository = BuildRepository();

        await repository.DeleteAsync(BatchId, requestCts.Token);

        capturedToken.Should().NotBeNull();
        capturedToken!.Value.CanBeCanceled.Should().BeTrue();
    }

    [Fact]
    public async Task DeleteAsync_NotFound_ReturnsSuccessIdempotent()
    {
        var requestCts = new CancellationTokenSource();

        _container.DeleteItemAsync<SagaDocument>(
            BatchId,
            Arg.Any<PartitionKey>(),
            cancellationToken: Arg.Any<CancellationToken>())
            .Returns<Task>(_ => throw new CosmosException("Not found", HttpStatusCode.NotFound, 0, "act-1", 0));

        var repository = BuildRepository();

        var act = async () => await repository.DeleteAsync(BatchId, requestCts.Token);

        await act.Should().NotThrowAsync();
        await _container.Received(1).DeleteItemAsync<SagaDocument>(
            BatchId,
            Arg.Any<PartitionKey>(),
            cancellationToken: Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DeleteAsync_PassesExternalCtCorrectlyToRetryHelper()
    {
        var requestCts = new CancellationTokenSource();
        requestCts.Cancel();

        _container.DeleteItemAsync<SagaDocument>(
            BatchId,
            Arg.Any<PartitionKey>(),
            cancellationToken: Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Substitute.For<ItemResponse<SagaDocument>>()));

        var repository = BuildRepository();

        await repository.DeleteAsync(BatchId, requestCts.Token);

        requestCts.Token.IsCancellationRequested.Should().BeTrue();
    }

    [Fact]
    public async Task DeleteAsync_TransientCosmosException_Retries()
    {
        var requestCts = new CancellationTokenSource();
        var attempt = 0;

        _container.DeleteItemAsync<SagaDocument>(
            BatchId,
            Arg.Any<PartitionKey>(),
            cancellationToken: Arg.Any<CancellationToken>())
            .Returns<Task>(_ =>
            {
                attempt++;
                if (attempt == 1)
                    throw new CosmosException("transient", HttpStatusCode.ServiceUnavailable, 0, "act-1", 0);
                return Task.FromResult(Substitute.For<ItemResponse<SagaDocument>>());
            });

        var repository = BuildRepository();

        await repository.DeleteAsync(BatchId, requestCts.Token);

        attempt.Should().Be(2);
    }

    [Fact]
    public async Task DeleteAsync_ServerOwnedCancellationRetries_RequestCancellationDoesNot()
    {
        var requestCts = new CancellationTokenSource();
        var attempt = 0;

        _container.DeleteItemAsync<SagaDocument>(
            BatchId,
            Arg.Any<PartitionKey>(),
            cancellationToken: Arg.Any<CancellationToken>())
            .Returns<Task>(_ =>
            {
                attempt++;
                if (attempt == 1)
                {
                    using var internalCts = new CancellationTokenSource();
                    internalCts.Cancel();
                    throw new OperationCanceledException("internal deadline", internalCts.Token);
                }
                return Task.FromResult(Substitute.For<ItemResponse<SagaDocument>>());
            });

        var repository = BuildRepository();

        await repository.DeleteAsync(BatchId, requestCts.Token);

        attempt.Should().Be(2);
        requestCts.Token.IsCancellationRequested.Should().BeFalse();
    }

    [Fact]
    public async Task DeleteAsync_UsesSettingsDeadlineValue()
    {
        const int CustomDeadlineSeconds = 20;
        var customSettings = new CosmosSettings
        {
            Database = "cortexa-pipeline",
            BatchesContainer = "batches",
            SagaDeleteDeadlineSeconds = CustomDeadlineSeconds
        };

        var customClient = Substitute.For<CosmosClient>();
        var customContainer = Substitute.For<Container>();
        customClient.GetContainer(customSettings.Database, customSettings.BatchesContainer)
            .Returns(customContainer);

        CancellationToken? capturedToken = null;

        customContainer.DeleteItemAsync<SagaDocument>(
            BatchId,
            Arg.Any<PartitionKey>(),
            cancellationToken: Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                capturedToken = callInfo.Arg<CancellationToken>();
                var response = Substitute.For<ItemResponse<SagaDocument>>();
                return Task.FromResult(response);
            });

        var repository = new CosmosSagaRepository(customClient, Options.Create(customSettings), _retryPolicy);

        await repository.DeleteAsync(BatchId, CancellationToken.None);

        capturedToken.Should().NotBeNull();
        capturedToken!.Value.CanBeCanceled.Should().BeTrue();
    }

    [Fact]
    public async Task DeleteAsync_CorrectPartitionKey_Passed()
    {
        var requestCts = new CancellationTokenSource();
        PartitionKey? capturedKey = null;

        _container.DeleteItemAsync<SagaDocument>(
            BatchId,
            Arg.Any<PartitionKey>(),
            cancellationToken: Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                capturedKey = callInfo.Arg<PartitionKey>();
                var response = Substitute.For<ItemResponse<SagaDocument>>();
                return Task.FromResult(response);
            });

        var repository = BuildRepository();

        await repository.DeleteAsync(BatchId, requestCts.Token);

        capturedKey.Should().NotBeNull();
    }
}
