using System.Text.Json.Serialization.Metadata;
using Collector.Application.Ports;
using Collector.Application.Remote;
using Collector.Domain.Enums;
using Collector.Domain.Remote;
using Collector.Infrastructure.Http;
using Collector.Infrastructure.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Collector.Infrastructure.Remote.GitHub;

public sealed class GitHubRepositoryClient(
    IHttpClientFactory factory,
    IOptionsMonitor<RemoteSourceOptions> options,
    GitHubErrorMapper mapper,
    ILogger<GitHubRepositoryClient> logger) : IRemoteRepositoryClient
{
    private const string JsonAccept = "application/vnd.github+json";
    private const string RawAccept = "application/vnd.github.raw+json";
    private const string VersionHeader = "X-GitHub-Api-Version";
    private const string UserAgent = "CortexaCollector";
    private const string LinkHeader = "Link";
    private const string BlobType = "blob";
    private const string FallbackBranch = "main";
    private const long BytesPerKilobyte = 1024;

    public SourceType Provider => SourceType.Github;

    public async Task<IReadOnlyList<RemoteRepository>> ListRepositoriesAsync(
        string? scope,
        CancellationToken cancellationToken)
    {
        var http = CreateHttp();
        var first = RemoteUrl.Relative("user/repos?affiliation=owner,collaborator,organization_member&per_page=100");
        var wire = await CreatePager().CollectAsync(
            first,
            (uri, token) => FetchPageAsync(http, uri, GitHubJson.Default.ListGitHubRepoWire, token),
            cancellationToken);
        return [.. wire.Select(ToRepository).OfType<RemoteRepository>()];
    }

    public async Task<IReadOnlyList<RemoteBranch>> ListBranchesAsync(
        RemoteRepository repository,
        CancellationToken cancellationToken)
    {
        var http = CreateHttp();
        var first = RemoteUrl.Relative($"{RepoPath(repository)}/branches?per_page=100");
        var wire = await CreatePager().CollectAsync(
            first,
            (uri, token) => FetchPageAsync(http, uri, GitHubJson.Default.ListGitHubBranchWire, token),
            cancellationToken);
        return [.. wire.Select(ToBranch).OfType<RemoteBranch>()];
    }

    public async Task<RemoteTree> GetTreeAsync(
        RemoteRepository repository,
        string branch,
        CancellationToken cancellationToken)
    {
        var http = CreateHttp();
        var repoPath = RepoPath(repository);
        var reference = await GetJsonAsync(
            http,
            $"{repoPath}/git/ref/heads/{RemoteUrl.Path(branch)}",
            GitHubJson.Default.GitHubRefWire,
            cancellationToken);
        var sha = reference.Object?.Sha ?? throw Malformed();
        var tree = await GetJsonAsync(
            http,
            $"{repoPath}/git/trees/{RemoteUrl.Segment(sha)}?recursive=1",
            GitHubJson.Default.GitHubTreeWire,
            cancellationToken);
        var entries = (tree.Tree ?? []).Select(ToEntry).OfType<RemoteTreeEntry>().ToList();
        return new RemoteTree(sha, entries, tree.Truncated);
    }

    public async Task<RemoteBlob> OpenBlobAsync(
        RemoteRepository repository,
        string blobSha,
        CancellationToken cancellationToken)
    {
        using var request = NewRequest(RemoteUrl.Relative($"{RepoPath(repository)}/git/blobs/{RemoteUrl.Segment(blobSha)}"), RawAccept);
        return await CreateHttp().OpenBlobAsync(request, cancellationToken);
    }

    private static string RepoPath(RemoteRepository repository) =>
        $"repos/{RemoteUrl.Segment(repository.Owner)}/{RemoteUrl.Segment(repository.Name)}";

    private static RemoteSourceException Malformed() => new(RemoteFailureKind.Upstream, SourceType.Github);

    private static RemoteRepository? ToRepository(GitHubRepoWire wire)
    {
        if (string.IsNullOrEmpty(wire.Name) || string.IsNullOrEmpty(wire.Owner?.Login))
        {
            return null;
        }

        var owner = wire.Owner.Login;
        return new RemoteRepository(
            SourceType.Github,
            owner,
            null,
            wire.Name,
            wire.FullName ?? $"{owner}/{wire.Name}",
            wire.DefaultBranch ?? FallbackBranch,
            wire.HtmlUrl ?? string.Empty,
            wire.Size * BytesPerKilobyte,
            wire.IsPrivate);
    }

    private static RemoteBranch? ToBranch(GitHubBranchWire wire) =>
        string.IsNullOrEmpty(wire.Name) || string.IsNullOrEmpty(wire.Commit?.Sha)
            ? null
            : new RemoteBranch(wire.Name, wire.Commit.Sha);

    private static RemoteTreeEntry? ToEntry(GitHubTreeItemWire wire) =>
        wire.Type == BlobType && !string.IsNullOrEmpty(wire.Path) && !string.IsNullOrEmpty(wire.Sha)
            ? new RemoteTreeEntry(wire.Path, wire.Sha, wire.Size)
            : null;

    private static Uri? NextLink(HttpResponseMessage response) =>
        response.Headers.TryGetValues(LinkHeader, out var values) && response.RequestMessage?.RequestUri is { } current
            ? LinkHeaderParser.ParseNext(values, current)
            : null;

    private RemoteHttp CreateHttp() => new(factory.CreateClient(HttpClientNames.GitHub), mapper, SourceType.Github);

    private RemotePager CreatePager() => new(options.CurrentValue.MaxPages, logger);

    private HttpRequestMessage NewRequest(Uri uri, string accept)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.TryAddWithoutValidation("Accept", accept);
        request.Headers.TryAddWithoutValidation(VersionHeader, options.CurrentValue.GitHub.ApiVersion);
        request.Headers.UserAgent.ParseAdd(UserAgent);
        return request;
    }

    private async Task<RemotePage<T>> FetchPageAsync<T>(
        RemoteHttp http,
        Uri uri,
        JsonTypeInfo<List<T>> typeInfo,
        CancellationToken cancellationToken)
    {
        using var request = NewRequest(uri, JsonAccept);
        using var response = await http.SendCheckedAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken);
        var items = await http.ReadJsonAsync(response, typeInfo, cancellationToken);
        return new RemotePage<T>(items, NextLink(response));
    }

    private async Task<T> GetJsonAsync<T>(
        RemoteHttp http,
        string relative,
        JsonTypeInfo<T> typeInfo,
        CancellationToken cancellationToken)
    {
        using var request = NewRequest(RemoteUrl.Relative(relative), JsonAccept);
        using var response = await http.SendCheckedAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken);
        return await http.ReadJsonAsync(response, typeInfo, cancellationToken);
    }
}
