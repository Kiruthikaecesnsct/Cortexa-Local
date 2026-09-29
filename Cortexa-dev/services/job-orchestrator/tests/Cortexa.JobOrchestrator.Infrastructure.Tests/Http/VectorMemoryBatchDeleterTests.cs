using System.Net;
using System.Text;
using Cortexa.JobOrchestrator.Application.Models;
using Cortexa.JobOrchestrator.Infrastructure.Http;
using FluentAssertions;
using Xunit;

namespace Cortexa.JobOrchestrator.Infrastructure.Tests.Http;

public sealed class VectorMemoryBatchDeleterTests
{
    private const string BatchId = "batch-42";

    private static DeleteBatchContext Context() =>
        new(BatchId, new DeletionRequestOptions("op", "corr", Force: false), BatchAccess.Unrestricted);

    [Fact]
    public async Task DeleteAsync_DeletesVectors_ReturnsSuccessWithCount()
    {
        var handler = new StubHandler(HttpStatusCode.OK, "{\"deleted\": 12}");
        var deleter = CreateDeleter(handler);

        var result = await deleter.DeleteAsync(BatchId, Context(), CancellationToken.None);

        result.Success.Should().BeTrue();
        result.DeletedCount.Should().Be(12);
        result.StoreName.Should().Be("VectorMemory");
        handler.LastRequestUri!.AbsolutePath.Should().Be($"/asset/{BatchId}");
        handler.LastMethod.Should().Be(HttpMethod.Delete);
    }

    [Fact]
    public async Task DeleteAsync_NotFound_IsIdempotentSuccess()
    {
        var handler = new StubHandler(HttpStatusCode.NotFound, string.Empty);
        var deleter = CreateDeleter(handler);

        var result = await deleter.DeleteAsync(BatchId, Context(), CancellationToken.None);

        result.Success.Should().BeTrue();
        result.DeletedCount.Should().Be(0);
    }

    [Fact]
    public async Task DeleteAsync_ZeroDeleted_IsIdempotentSuccess()
    {
        var handler = new StubHandler(HttpStatusCode.OK, "{\"deleted\": 0}");
        var deleter = CreateDeleter(handler);

        var result = await deleter.DeleteAsync(BatchId, Context(), CancellationToken.None);

        result.Success.Should().BeTrue();
        result.DeletedCount.Should().Be(0);
    }

    [Fact]
    public async Task DeleteAsync_ServerError_ReturnsFailedResult()
    {
        var handler = new StubHandler(HttpStatusCode.InternalServerError, string.Empty);
        var deleter = CreateDeleter(handler);

        var result = await deleter.DeleteAsync(BatchId, Context(), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Error.Should().Be("500");
    }

    [Fact]
    public async Task DeleteAsync_HttpRequestException_ReturnsFailedResult()
    {
        var handler = new StubHandler(new HttpRequestException("connection refused"));
        var deleter = CreateDeleter(handler);

        var result = await deleter.DeleteAsync(BatchId, Context(), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Error.Should().Be("connection refused");
    }

    [Fact]
    public async Task DeleteAsync_Timeout_ReturnsFailedResult()
    {
        var handler = new StubHandler(new TaskCanceledException("timed out"));
        var deleter = CreateDeleter(handler);

        var result = await deleter.DeleteAsync(BatchId, Context(), CancellationToken.None);

        result.Success.Should().BeFalse();
    }

    [Fact]
    public async Task DeleteAsync_CallerCancellation_Propagates()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var handler = new StubHandler(new TaskCanceledException("cancelled"));
        var deleter = CreateDeleter(handler);

        var act = async () => await deleter.DeleteAsync(BatchId, Context(), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task DeleteAsync_MissingBaseAddress_ReturnsFailedResultWithoutThrowing()
    {
        var client = new HttpClient(new StubHandler(HttpStatusCode.OK, "{\"deleted\": 0}"));
        var deleter = new VectorMemoryBatchDeleter(client);

        var result = await deleter.DeleteAsync(BatchId, Context(), CancellationToken.None);

        result.Success.Should().BeFalse();
    }

    private static VectorMemoryBatchDeleter CreateDeleter(StubHandler handler)
    {
        var client = new HttpClient(handler) { BaseAddress = new Uri("http://vector-router") };
        return new VectorMemoryBatchDeleter(client);
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _statusCode;
        private readonly string _body = string.Empty;
        private readonly Exception? _exception;

        public StubHandler(HttpStatusCode statusCode, string body)
        {
            _statusCode = statusCode;
            _body = body;
        }

        public StubHandler(Exception exception)
        {
            _exception = exception;
        }

        public Uri? LastRequestUri { get; private set; }
        public HttpMethod? LastMethod { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequestUri = request.RequestUri;
            LastMethod = request.Method;

            if (_exception is not null)
                return Task.FromException<HttpResponseMessage>(_exception);

            var response = new HttpResponseMessage(_statusCode)
            {
                Content = new StringContent(_body, Encoding.UTF8, "application/json")
            };
            return Task.FromResult(response);
        }
    }
}
