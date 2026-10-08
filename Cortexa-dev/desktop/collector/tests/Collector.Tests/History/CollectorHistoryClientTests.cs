using System.Net;
using Collector.Application.Ports;
using Collector.Domain.History;
using Collector.Infrastructure.History;
using Collector.Infrastructure.Options;
using Collector.Tests.Support;
using Microsoft.Extensions.Logging.Abstractions;

namespace Collector.Tests.History;

public sealed class CollectorHistoryClientTests
{
    private const string BaseUrl = "https://server.example/";
    private const int RetryDelayMs = 1;
    private const int TimeoutSeconds = 15;
    private const int ShortTimeoutSeconds = 1;
    private const int ExpectedAttemptsWithRetries = 3;

    private const string BatchesJson =
        "[{\"batch_id\":\"srv-1\",\"batch_name\":\"Weekly\",\"created_at\":\"2026-10-07T00:00:00+00:00\"," +
        "\"state\":\"InProgress\",\"stage\":\"Harvested\",\"extraction_completed_count\":2,\"extraction_total_count\":3," +
        "\"evidence_completed_count\":1,\"embedding_completed_count\":4,\"embedding_total_count\":5," +
        "\"harvesting_completed_count\":2,\"harvesting_total_count\":3,\"seeding_completed_count\":1,\"seeding_total_count\":3}]";

    private const string ResultsJson =
        "{\"batch_id\":\"srv-1\",\"candidates\":[{\"candidate_id\":\"c1\",\"engine\":\"harvesting\",\"title\":\"Cache\"," +
        "\"kind\":\"Mature\",\"evidence_count\":3,\"score\":68.5,\"patentability\":72,\"knowledge_links\":[{" +
        "\"knowledge_item\":{\"id\":\"doc|0\",\"kind\":\"key_content\",\"title\":\"K\",\"summary\":\"S\"}," +
        "\"source\":{\"document_id\":\"d1\",\"page_number\":4}}]}]}";

    private static CollectorHistoryClient Client(
        StubHttpHandler handler,
        int timeoutSeconds = TimeoutSeconds,
        int[]? delays = null)
    {
        var options = new CollectorServerOptions
        {
            BaseUrl = BaseUrl,
            ReadTimeoutSeconds = timeoutSeconds,
            ReadRetryDelaysMs = delays ?? [RetryDelayMs, RetryDelayMs],
        };
        var monitor = new StaticMonitor<CollectorServerOptions>(options);
        return new CollectorHistoryClient(
            new StubHttpClientFactory(handler),
            monitor,
            new HistoryRetryPolicy(monitor, TimeProvider.System),
            NullLogger<CollectorHistoryClient>.Instance);
    }

    private static StubHttpHandler Sequence(params HttpStatusCode[] statuses)
    {
        var index = 0;
        return new StubHttpHandler((_, _) =>
        {
            var status = statuses[Math.Min(Interlocked.Increment(ref index) - 1, statuses.Length - 1)];
            return Task.FromResult(StubHttpHandler.Json(status, status == HttpStatusCode.OK ? ResultsJson : "{}"));
        });
    }

    [Fact]
    public async Task ListBatchesAsync_Success_ParsesStageAndCounts()
    {
        var handler = StubHttpHandler.Returning(HttpStatusCode.OK, BatchesJson);

        var batches = await Client(handler).ListBatchesAsync(TestSupport.Ct);

        var batch = Assert.Single(batches);
        Assert.Equal("srv-1", batch.BatchId);
        Assert.Equal(BatchStage.Harvested, batch.Stage);
        Assert.Equal(2, batch.HarvestingCompletedCount);
        Assert.Equal(3, batch.SeedingTotalCount);
        var sent = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Get, sent.Method);
        Assert.Equal("https://server.example/collector/batches", sent.Uri!.AbsoluteUri);
    }

    [Fact]
    public async Task GetResultsAsync_Success_ParsesCandidatesAndLinks()
    {
        var handler = StubHttpHandler.Returning(HttpStatusCode.OK, ResultsJson);

        var results = await Client(handler).GetResultsAsync("srv-1", TestSupport.Ct);

        var candidate = Assert.Single(results!.Candidates);
        Assert.Equal(68.5, candidate.Score);
        Assert.Equal(72, candidate.Patentability);
        var link = Assert.Single(candidate.KnowledgeLinks);
        Assert.Equal("d1", link.Source.DocumentId);
        Assert.Equal(4, link.Source.PageNumber);
        Assert.Equal(Collector.Domain.Enums.KnowledgeKind.KeyContent, link.KnowledgeItem.Kind);
    }

    [Fact]
    public async Task GetResultsAsync_IdWithReservedCharacters_IsEscapedInThePath()
    {
        var handler = StubHttpHandler.Returning(HttpStatusCode.OK, ResultsJson);

        await Client(handler).GetResultsAsync("a/b c?d", TestSupport.Ct);

        var sent = Assert.Single(handler.Requests);
        Assert.Equal("https://server.example/collector/batches/a%2Fb%20c%3Fd/results", sent.Uri!.AbsoluteUri);
    }

    [Fact]
    public async Task GetResultsAsync_NotFound_ReturnsNullWithoutRetry()
    {
        var handler = StubHttpHandler.Returning(HttpStatusCode.NotFound);

        var results = await Client(handler).GetResultsAsync("srv-1", TestSupport.Ct);

        Assert.Null(results);
        Assert.Single(handler.Requests);
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.RequestTimeout)]
    public async Task GetResultsAsync_TransientStatusThenSuccess_Retries(HttpStatusCode transient)
    {
        var handler = Sequence(transient, HttpStatusCode.OK);

        var results = await Client(handler).GetResultsAsync("srv-1", TestSupport.Ct);

        Assert.NotNull(results);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task GetResultsAsync_PersistentServerError_StopsAfterTwoRetries()
    {
        var handler = Sequence(HttpStatusCode.InternalServerError);

        var exception = await Assert.ThrowsAsync<BatchHistoryException>(
            () => Client(handler).GetResultsAsync("srv-1", TestSupport.Ct));

        Assert.Equal(500, exception.StatusCode);
        Assert.False(exception.IsAuthFailure);
        Assert.Equal(ExpectedAttemptsWithRetries, handler.Requests.Count);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Conflict)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    public async Task GetResultsAsync_ClientError_IsNeverRetried(HttpStatusCode status)
    {
        var handler = Sequence(status);

        var exception = await Assert.ThrowsAsync<BatchHistoryException>(
            () => Client(handler).GetResultsAsync("srv-1", TestSupport.Ct));

        Assert.Equal((int)status, exception.StatusCode);
        Assert.Single(handler.Requests);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task ListBatchesAsync_AuthStatus_FlagsAuthFailureWithoutRetry(HttpStatusCode status)
    {
        var handler = Sequence(status);

        var exception = await Assert.ThrowsAsync<BatchHistoryException>(() => Client(handler).ListBatchesAsync(TestSupport.Ct));

        Assert.True(exception.IsAuthFailure);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task GetResultsAsync_NetworkErrorThenSuccess_Retries()
    {
        var calls = 0;
        var handler = new StubHttpHandler((_, _) => ++calls == 1
            ? throw new HttpRequestException("connection refused")
            : Task.FromResult(StubHttpHandler.Json(HttpStatusCode.OK, ResultsJson)));

        var results = await Client(handler).GetResultsAsync("srv-1", TestSupport.Ct);

        Assert.NotNull(results);
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task GetResultsAsync_PersistentNetworkError_ThrowsUnreachable()
    {
        var handler = new StubHttpHandler((_, _) => throw new HttpRequestException("connection refused"));

        var exception = await Assert.ThrowsAsync<BatchHistoryException>(
            () => Client(handler).GetResultsAsync("srv-1", TestSupport.Ct));

        Assert.Null(exception.StatusCode);
        Assert.False(exception.IsAuthFailure);
        Assert.Equal(ExpectedAttemptsWithRetries, handler.Requests.Count);
    }

    [Fact]
    public async Task GetResultsAsync_NoConfiguredDelays_DoesNotRetry()
    {
        var handler = Sequence(HttpStatusCode.InternalServerError);

        await Assert.ThrowsAsync<BatchHistoryException>(
            () => Client(handler, delays: []).GetResultsAsync("srv-1", TestSupport.Ct));

        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task GetResultsAsync_CallerCancelled_PropagatesCancellation()
    {
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        var handler = StubHttpHandler.Returning(HttpStatusCode.OK, ResultsJson);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => Client(handler).GetResultsAsync("srv-1", cancelled.Token));
    }

    [Fact]
    public async Task GetResultsAsync_CancelledDuringRetryDelay_PropagatesCancellation()
    {
        using var cancel = new CancellationTokenSource();
        var handler = new StubHttpHandler((_, _) =>
        {
            cancel.CancelAfter(TimeSpan.Zero);
            return Task.FromResult(StubHttpHandler.Json(HttpStatusCode.InternalServerError, "{}"));
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => Client(handler, delays: [TimeoutSeconds * 1000]).GetResultsAsync("srv-1", cancel.Token));
    }

    [Fact]
    public async Task GetResultsAsync_ServerNeverAnswers_FailsWithTimeoutError()
    {
        var handler = new StubHttpHandler(async (_, token) =>
        {
            await Task.Delay(Timeout.Infinite, token);
            return StubHttpHandler.Json(HttpStatusCode.OK, ResultsJson);
        });

        var exception = await Assert.ThrowsAsync<BatchHistoryException>(
            () => Client(handler, ShortTimeoutSeconds).GetResultsAsync("srv-1", TestSupport.Ct));

        Assert.Null(exception.StatusCode);
    }

    [Fact]
    public async Task ListBatchesAsync_MalformedBody_ThrowsReadableError()
    {
        var handler = StubHttpHandler.Returning(HttpStatusCode.OK, "not json");

        var exception = await Assert.ThrowsAsync<BatchHistoryException>(() => Client(handler).ListBatchesAsync(TestSupport.Ct));

        Assert.Equal(200, exception.StatusCode);
    }

    [Fact]
    public async Task ListBatchesAsync_NotFound_Throws()
    {
        var handler = StubHttpHandler.Returning(HttpStatusCode.NotFound);

        var exception = await Assert.ThrowsAsync<BatchHistoryException>(() => Client(handler).ListBatchesAsync(TestSupport.Ct));

        Assert.Equal(404, exception.StatusCode);
    }

    [Fact]
    public async Task ListBatchesAsync_UsesTheCollectorServerHttpClient()
    {
        var handler = StubHttpHandler.Returning(HttpStatusCode.OK, "[]");
        var factory = new StubHttpClientFactory(handler);
        var options = new StaticMonitor<CollectorServerOptions>(new CollectorServerOptions { BaseUrl = BaseUrl });
        var client = new CollectorHistoryClient(
            factory,
            options,
            new HistoryRetryPolicy(options, TimeProvider.System),
            NullLogger<CollectorHistoryClient>.Instance);

        await client.ListBatchesAsync(TestSupport.Ct);

        Assert.Equal(Collector.Infrastructure.Http.HttpClientNames.CollectorServer, factory.LastName);
    }
}
