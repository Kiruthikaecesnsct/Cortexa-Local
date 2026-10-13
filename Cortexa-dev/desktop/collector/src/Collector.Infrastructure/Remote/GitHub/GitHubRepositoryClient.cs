using Collector.Application.Ports;
using Collector.Application.Remote;
using Collector.Application.Remote.Auth;
using Collector.Domain.Enums;
using Collector.Domain.Remote;

namespace Collector.Infrastructure.Remote.GitHub;

public sealed class GitHubRepositoryClient(
    GitHubApiTransport api,
    GitHubInstallationLister installations,
    IRemoteAuthState authState) : IRemoteRepositoryClient
{
    private const string BlobType = "blob";
    private const string UserReposPath = "user/repos?affiliation=owner,collaborator,organization_member&per_page=100";

    public SourceType Provider => SourceType.Github;

    public async Task<IReadOnlyList<RemoteRepository>> ListRepositoriesAsync(
        string? scope,
        CancellationToken cancellationToken)
    {
        if (authState.MethodFor(SourceType.Github) == RemoteAuthMethod.GitHubApp)
        {
            return await installations.ListRepositoriesAsync(scope, cancellationToken);
        }

        if (string.IsNullOrWhiteSpace(scope))
        {
            return await CollectRepositoriesAsync(UserReposPath, cancellationToken);
        }

        var organization = RemoteUrl.Segment(scope);
        try
        {
            return await CollectRepositoriesAsync(
                $"orgs/{organization}/repos?type=all&per_page=100",
                cancellationToken);
        }
        catch (RemoteSourceException exception) when (exception.Kind == RemoteFailureKind.NotFound)
        {
            return await CollectRepositoriesAsync(
                $"users/{organization}/repos?type=owner&per_page=100",
                cancellationToken);
        }
    }

    private async Task<IReadOnlyList<RemoteRepository>> CollectRepositoriesAsync(
        string relative,
        CancellationToken cancellationToken)
    {
        var http = api.CreateHttp();
        var reader = new PageReader<List<GitHubRepoWire>, GitHubRepoWire>(GitHubJson.Default.ListGitHubRepoWire, body => body);
        var wire = await api.CreatePager().CollectAsync(
            RemoteUrl.Relative(relative),
            (uri, token) => api.FetchPageAsync(http, uri, reader, token),
            cancellationToken);
        return [.. wire.Select(GitHubRepoMapper.ToRepository).OfType<RemoteRepository>()];
    }

    public async Task<IReadOnlyList<RemoteBranch>> ListBranchesAsync(
        RemoteRepository repository,
        CancellationToken cancellationToken)
    {
        var http = api.CreateHttp();
        var first = RemoteUrl.Relative($"{RepoPath(repository)}/branches?per_page=100");
        var reader = new PageReader<List<GitHubBranchWire>, GitHubBranchWire>(GitHubJson.Default.ListGitHubBranchWire, body => body);
        var wire = await api.CreatePager().CollectAsync(
            first,
            (uri, token) => api.FetchPageAsync(http, uri, reader, token),
            cancellationToken);
        return [.. wire.Select(ToBranch).OfType<RemoteBranch>()];
    }

    public async Task<RemoteTree> GetTreeAsync(
        RemoteRepository repository,
        string branch,
        CancellationToken cancellationToken)
    {
        var http = api.CreateHttp();
        var repoPath = RepoPath(repository);
        var reference = await api.GetJsonAsync(
            http,
            $"{repoPath}/git/ref/heads/{RemoteUrl.Path(branch)}",
            GitHubJson.Default.GitHubRefWire,
            cancellationToken);
        var sha = reference.Object?.Sha ?? throw Malformed();
        var tree = await api.GetJsonAsync(
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
        using var request = api.NewRequest(
            RemoteUrl.Relative($"{RepoPath(repository)}/git/blobs/{RemoteUrl.Segment(blobSha)}"),
            GitHubApiTransport.RawAccept);
        return await api.CreateHttp().OpenBlobAsync(request, cancellationToken);
    }

    private static string RepoPath(RemoteRepository repository) =>
        $"repos/{RemoteUrl.Segment(repository.Owner)}/{RemoteUrl.Segment(repository.Name)}";

    private static RemoteSourceException Malformed() => new(RemoteFailureKind.Upstream, SourceType.Github);

    private static RemoteBranch? ToBranch(GitHubBranchWire wire) =>
        string.IsNullOrEmpty(wire.Name) || string.IsNullOrEmpty(wire.Commit?.Sha)
            ? null
            : new RemoteBranch(wire.Name, wire.Commit.Sha, wire.IsProtected);

    private static RemoteTreeEntry? ToEntry(GitHubTreeItemWire wire) =>
        wire.Type == BlobType && !string.IsNullOrEmpty(wire.Path) && !string.IsNullOrEmpty(wire.Sha)
            ? new RemoteTreeEntry(wire.Path, wire.Sha, wire.Size)
            : null;
}
