using System.Text.Json.Serialization.Metadata;
using Collector.Domain.Enums;
using Collector.Infrastructure.Http;
using Collector.Infrastructure.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Collector.Infrastructure.Remote.GitHub;

public sealed class GitHubApiTransport(
    IHttpClientFactory factory,
    IOptionsMonitor<RemoteSourceOptions> options,
    GitHubErrorMapper mapper,
    ILogger<GitHubApiTransport> logger)
{
    internal const string JsonAccept = "application/vnd.github+json";
    internal const string RawAccept = "application/vnd.github.raw+json";

    private const string VersionHeader = "X-GitHub-Api-Version";
    private const string UserAgent = "CortexaCollector";
    private const string LinkHeader = "Link";

    internal RemoteHttp CreateHttp() =>
        new(factory.CreateClient(HttpClientNames.GitHub), mapper, SourceType.Github, ReadIdleTimeout());

    internal RemotePager CreatePager() => new(options.CurrentValue.MaxPages, logger);

    internal HttpRequestMessage NewRequest(Uri uri, string accept)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.TryAddWithoutValidation("Accept", accept);
        request.Headers.TryAddWithoutValidation(VersionHeader, options.CurrentValue.GitHub.ApiVersion);
        request.Headers.UserAgent.ParseAdd(UserAgent);
        return request;
    }

    internal async Task<RemotePage<TItem>> FetchPageAsync<TBody, TItem>(
        RemoteHttp http,
        Uri uri,
        PageReader<TBody, TItem> reader,
        CancellationToken cancellationToken)
    {
        using var request = NewRequest(uri, JsonAccept);
        request.Options.Set(RemoteRequestOptions.BufferBody, true);
        using var response = await http.SendCheckedAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken);
        var body = await http.ReadJsonAsync(response, reader.TypeInfo, cancellationToken);
        return new RemotePage<TItem>(reader.Items(body), NextLink(response));
    }

    internal async Task<T> GetJsonAsync<T>(
        RemoteHttp http,
        string relative,
        JsonTypeInfo<T> typeInfo,
        CancellationToken cancellationToken)
    {
        using var request = NewRequest(RemoteUrl.Relative(relative), JsonAccept);
        using var response = await http.SendCheckedAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken);
        return await http.ReadJsonAsync(response, typeInfo, cancellationToken);
    }

    private static Uri? NextLink(HttpResponseMessage response) =>
        response.Headers.TryGetValues(LinkHeader, out var values) && response.RequestMessage?.RequestUri is { } current
            ? LinkHeaderParser.ParseNext(values, current)
            : null;

    private TimeSpan ReadIdleTimeout() => TimeSpan.FromSeconds(options.CurrentValue.TimeoutSeconds);
}

internal sealed record PageReader<TBody, TItem>(JsonTypeInfo<TBody> TypeInfo, Func<TBody, IReadOnlyList<TItem>> Items);
