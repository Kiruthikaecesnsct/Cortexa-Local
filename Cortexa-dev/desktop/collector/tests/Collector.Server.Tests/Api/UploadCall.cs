using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Collector.Domain.Serialization;
using Collector.Domain.Upload;
using Collector.Server.Api.Endpoints;
using Collector.Server.Tests.Upload;

namespace Collector.Server.Tests.Api;

internal sealed record UploadCall
{
    public const string Path = "/collector/batches/knowledge";

    public string Body { get; init; } = Serialize(UploadRequests.Valid());

    public string? Token { get; init; } = TestJwtFactory.Create();

    public string? IdempotencyKey { get; init; } = TestIdentity.IdempotencyKey;

    public static string Serialize(KnowledgeUploadRequest request) =>
        JsonSerializer.Serialize(request, CollectorJson.Options);

    public UploadCall WithRequest(KnowledgeUploadRequest request) => this with { Body = Serialize(request) };

    public HttpRequestMessage ToRequest()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, Path)
        {
            Content = new StringContent(Body, Encoding.UTF8, "application/json")
        };
        if (Token is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Token);
        }

        if (IdempotencyKey is not null)
        {
            request.Headers.TryAddWithoutValidation(IdempotencyKeys.HeaderName, IdempotencyKey);
        }

        return request;
    }
}
