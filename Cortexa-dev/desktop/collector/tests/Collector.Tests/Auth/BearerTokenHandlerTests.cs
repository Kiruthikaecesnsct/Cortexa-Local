using System.Net;
using System.Text;
using Collector.Infrastructure.Auth;
using Collector.Tests.Support;

namespace Collector.Tests.Auth;

public class BearerTokenHandlerTests
{
    private const string Uri = "https://server.example/upload";
    private const string OldToken = "old-token";
    private const string NewToken = "new-token";

    private readonly FakeSession _session = new() { Token = OldToken, RefreshedToken = NewToken };

    private sealed class ScriptedHandler(params HttpStatusCode[] statuses) : HttpMessageHandler
    {
        private int _index;

        public List<string?> Authorizations { get; } = [];

        public List<string?> Bodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Authorizations.Add(request.Headers.Authorization?.ToString());
            Bodies.Add(request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken));
            return new HttpResponseMessage(statuses[_index++]);
        }
    }

    private (HttpClient Client, ScriptedHandler Inner) Create(params HttpStatusCode[] statuses)
    {
        var inner = new ScriptedHandler(statuses);
        var handler = new BearerTokenHandler(_session) { InnerHandler = inner };
        return (new HttpClient(handler), inner);
    }

    [Fact]
    public async Task Attaches_the_bearer_token()
    {
        var (client, inner) = Create(HttpStatusCode.OK);

        using var response = await client.GetAsync(Uri, TestSupport.Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal([$"Bearer {OldToken}"], inner.Authorizations);
        Assert.Equal(0, _session.RefreshAfterUnauthorizedCalls);
    }

    [Fact]
    public async Task Retries_once_with_the_new_token_after_401()
    {
        var (client, inner) = Create(HttpStatusCode.Unauthorized, HttpStatusCode.OK);

        using var response = await client.GetAsync(Uri, TestSupport.Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal([$"Bearer {OldToken}", $"Bearer {NewToken}"], inner.Authorizations);
        Assert.Equal(1, _session.RefreshAfterUnauthorizedCalls);
        Assert.Equal(0, _session.MarkExpiredCalls);
    }

    [Fact]
    public async Task Marks_session_expired_when_retry_is_also_401()
    {
        var (client, inner) = Create(HttpStatusCode.Unauthorized, HttpStatusCode.Unauthorized);

        using var response = await client.GetAsync(Uri, TestSupport.Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(2, inner.Authorizations.Count);
        Assert.Equal(1, _session.RefreshAfterUnauthorizedCalls);
        Assert.Equal(1, _session.MarkExpiredCalls);
    }

    [Fact]
    public async Task Returns_original_401_without_retry_when_refresh_yields_no_token()
    {
        _session.RefreshedToken = null;
        var (client, inner) = Create(HttpStatusCode.Unauthorized);

        using var response = await client.GetAsync(Uri, TestSupport.Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Single(inner.Authorizations);
    }

    [Fact]
    public async Task Resends_the_request_body_on_retry()
    {
        const string Body = "{\"payload\":1}";
        var (client, inner) = Create(HttpStatusCode.Unauthorized, HttpStatusCode.OK);
        using var content = new StringContent(Body, Encoding.UTF8, "application/json");

        using var response = await client.PostAsync(Uri, content, TestSupport.Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal([Body, Body], inner.Bodies);
    }

    [Fact]
    public async Task Sends_without_authorization_when_signed_out()
    {
        _session.Token = null;
        var (client, inner) = Create(HttpStatusCode.Unauthorized);

        using var response = await client.GetAsync(Uri, TestSupport.Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal([(string?)null], inner.Authorizations);
        Assert.Equal(0, _session.RefreshAfterUnauthorizedCalls);
    }
}
