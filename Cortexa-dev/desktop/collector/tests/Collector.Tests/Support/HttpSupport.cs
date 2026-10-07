using System.Net;
using System.Text;

namespace Collector.Tests.Support;

internal sealed record CapturedRequest(HttpMethod Method, Uri? Uri, IReadOnlyDictionary<string, string> Headers, string Body);

internal sealed class StubHttpHandler(Func<CapturedRequest, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
{
    private readonly List<CapturedRequest> _requests = [];

    public IReadOnlyList<CapturedRequest> Requests
    {
        get
        {
            lock (_requests)
            {
                return [.. _requests];
            }
        }
    }

    public static StubHttpHandler Returning(HttpStatusCode status, string body = "{}") =>
        new((_, _) => Task.FromResult(Json(status, body)));

    public static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
        var headers = request.Headers.ToDictionary(header => header.Key, header => string.Join(',', header.Value), StringComparer.OrdinalIgnoreCase);
        var captured = new CapturedRequest(request.Method, request.RequestUri, headers, body);
        lock (_requests)
        {
            _requests.Add(captured);
        }

        return await respond(captured, cancellationToken);
    }
}

internal sealed class StubHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
{
    public string? LastName { get; private set; }

    public HttpClient CreateClient(string name)
    {
        LastName = name;
        return new HttpClient(handler, disposeHandler: false);
    }
}
