using System.Net;
using Collector.Application.Ports;
using Collector.Application.Upload;
using Collector.Domain.Upload;
using Collector.Infrastructure.Options;
using Collector.Infrastructure.Upload;
using Collector.Tests.Support;
using Microsoft.Extensions.Logging.Abstractions;

namespace Collector.Tests.Upload;

public sealed class CollectorUploadClientTests
{
    private const string BaseUrl = "https://server.example/";
    private const string Key = "0199-idempotency-key";
    private const string ResultJson = "{\"batch_id\":\"srv-7\",\"document_ids\":[\"d1\",\"d2\"]}";

    private static readonly KnowledgeUploadRequest Request = new()
    {
        BatchName = "Weekly upload",
        Collector = UploadData.Collector,
        Documents = [UploadData.Document("doc-1")],
    };

    private static readonly UploadPayload Payload = UploadPayload.From(Request);

    private static CollectorUploadClient Client(StubHttpHandler handler) => new(
        new StubHttpClientFactory(handler),
        new StaticMonitor<CollectorServerOptions>(new CollectorServerOptions { BaseUrl = BaseUrl }),
        NullLogger<CollectorUploadClient>.Instance);

    private static async Task<KnowledgeUploadException> FailureAsync(StubHttpHandler handler) =>
        await Assert.ThrowsAsync<KnowledgeUploadException>(() => Client(handler).UploadAsync(Payload, Key, TestSupport.Ct));

    [Fact]
    public async Task UploadAsync_Request_PostsJsonWithIdempotencyKeyToKnowledgeEndpoint()
    {
        var handler = StubHttpHandler.Returning(HttpStatusCode.Created, ResultJson);

        await Client(handler).UploadAsync(Payload, Key, TestSupport.Ct);

        var sent = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, sent.Method);
        Assert.Equal("https://server.example/collector/batches/knowledge", sent.Uri!.ToString());
        Assert.Equal(Key, sent.Headers["Idempotency-Key"]);
        Assert.Contains("\"batch_name\":\"Weekly upload\"", sent.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UploadAsync_Payload_SendsStoredBytesVerbatimWithJsonUtf8ContentType()
    {
        var handler = StubHttpHandler.Returning(HttpStatusCode.Created, ResultJson);
        var stored = new UploadPayload(System.Text.Encoding.UTF8.GetBytes("{\"batch_name\":\"frozen\"}"), "hash");

        await Client(handler).UploadAsync(stored, Key, TestSupport.Ct);

        var sent = Assert.Single(handler.Requests);
        Assert.Equal(stored.Body, sent.BodyBytes);
        Assert.Equal("application/json; charset=utf-8", sent.ContentType);
        Assert.Equal(Key, sent.Headers["Idempotency-Key"]);
    }

    [Theory]
    [InlineData(HttpStatusCode.Created)]
    [InlineData(HttpStatusCode.OK)]
    public async Task UploadAsync_SuccessStatus_ReturnsResult(HttpStatusCode status)
    {
        var handler = StubHttpHandler.Returning(status, ResultJson);

        var result = await Client(handler).UploadAsync(Payload, Key, TestSupport.Ct);

        Assert.Equal("srv-7", result.BatchId);
        Assert.Equal(["d1", "d2"], result.DocumentIds);
    }

    [Fact]
    public async Task UploadAsync_ClientErrorWithBody_ThrowsStatusAndCode()
    {
        var handler = StubHttpHandler.Returning(HttpStatusCode.BadRequest, "{\"error\":\"validation_failed\",\"message\":\"Bad\",\"errors\":[{\"field\":\"title\",\"message\":\"too long\"}]}");

        var exception = await FailureAsync(handler);

        Assert.Equal((int)HttpStatusCode.BadRequest, exception.StatusCode);
        Assert.Equal("validation_failed", exception.ErrorCode);
        Assert.Contains("title: too long", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UploadAsync_NonJsonErrorBody_ThrowsStatusWithNullCode()
    {
        var handler = new StubHttpHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadGateway) { Content = new StringContent("<html>oops</html>") }));

        var exception = await FailureAsync(handler);

        Assert.Equal((int)HttpStatusCode.BadGateway, exception.StatusCode);
        Assert.Null(exception.ErrorCode);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.Created)]
    public async Task UploadAsync_BodyWithUnsupportedCharset_ThrowsKnowledgeUploadException(HttpStatusCode status)
    {
        var handler = new StubHttpHandler((_, _) => Task.FromResult(new HttpResponseMessage(status) { Content = HtmlWithUnsupportedCharset() }));

        var exception = await FailureAsync(handler);

        Assert.Equal((int)status, exception.StatusCode);
        Assert.Null(exception.ErrorCode);
    }

    private static ByteArrayContent HtmlWithUnsupportedCharset()
    {
        var content = new ByteArrayContent("<html>gateway</html>"u8.ToArray());
        content.Headers.TryAddWithoutValidation("Content-Type", "text/html; charset=x-not-a-charset");
        return content;
    }

    [Fact]
    public async Task UploadAsync_SuccessWithUnreadableBody_ThrowsWithStatusAndNoCode()
    {
        var handler = StubHttpHandler.Returning(HttpStatusCode.Created, "not json");

        var exception = await FailureAsync(handler);

        Assert.Equal((int)HttpStatusCode.Created, exception.StatusCode);
        Assert.Null(exception.ErrorCode);
    }

    [Fact]
    public async Task UploadAsync_ConnectionFailure_ThrowsWithNullStatus()
    {
        var handler = new StubHttpHandler((_, _) => throw new HttpRequestException("refused"));

        var exception = await FailureAsync(handler);

        Assert.Null(exception.StatusCode);
        Assert.IsType<HttpRequestException>(exception.InnerException);
    }

    [Fact]
    public async Task UploadAsync_TimeoutWithoutCallerCancel_ThrowsWithNullStatus()
    {
        var handler = new StubHttpHandler((_, _) => throw new TaskCanceledException("timed out"));

        var exception = await FailureAsync(handler);

        Assert.Null(exception.StatusCode);
    }

    [Fact]
    public async Task UploadAsync_CallerCancels_PropagatesOperationCanceled()
    {
        using var source = CancellationTokenSource.CreateLinkedTokenSource(TestSupport.Ct);
        var handler = new StubHttpHandler(async (_, token) =>
        {
            await source.CancelAsync();
            await Task.Delay(Timeout.Infinite, token);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Client(handler).UploadAsync(Payload, Key, source.Token));
    }
}
