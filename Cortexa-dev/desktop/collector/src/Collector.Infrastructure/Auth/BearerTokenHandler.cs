using System.Net;
using System.Net.Http.Headers;
using Collector.Application.Auth;

namespace Collector.Infrastructure.Auth;

public sealed class BearerTokenHandler(IAccessTokenProvider tokens) : DelegatingHandler
{
    private const string BearerScheme = "Bearer";

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var token = await tokens.GetAccessTokenAsync(cancellationToken);
        if (token is null)
        {
            return await base.SendAsync(request, cancellationToken);
        }

        if (request.Content is not null)
        {
            await request.Content.LoadIntoBufferAsync(cancellationToken);
        }

        var response = await SendWithTokenAsync(request, token, cancellationToken);
        return response.StatusCode == HttpStatusCode.Unauthorized
            ? await RetryOnceAsync(request, response, token, cancellationToken)
            : response;
    }

    private async Task<HttpResponseMessage> RetryOnceAsync(
        HttpRequestMessage original,
        HttpResponseMessage rejected,
        string rejectedToken,
        CancellationToken cancellationToken)
    {
        var fresh = await tokens.RefreshAfterUnauthorizedAsync(rejectedToken, cancellationToken);
        if (fresh is null)
        {
            return rejected;
        }

        rejected.Dispose();
        var retry = await SendWithTokenAsync(Clone(original), fresh, cancellationToken);
        if (retry.StatusCode == HttpStatusCode.Unauthorized)
        {
            await tokens.MarkExpiredAsync(cancellationToken);
        }

        return retry;
    }

    private Task<HttpResponseMessage> SendWithTokenAsync(
        HttpRequestMessage request,
        string token,
        CancellationToken cancellationToken)
    {
        request.Headers.Authorization = new AuthenticationHeaderValue(BearerScheme, token);
        return base.SendAsync(request, cancellationToken);
    }

    private static HttpRequestMessage Clone(HttpRequestMessage source)
    {
        var clone = new HttpRequestMessage(source.Method, source.RequestUri)
        {
            Content = source.Content,
            Version = source.Version,
            VersionPolicy = source.VersionPolicy,
        };

        foreach (var header in source.Headers.Where(h => h.Key != "Authorization"))
        {
            clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        foreach (var option in (IDictionary<string, object?>)source.Options)
        {
            ((IDictionary<string, object?>)clone.Options)[option.Key] = option.Value;
        }

        return clone;
    }
}
