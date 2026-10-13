using Collector.Domain.Remote;

namespace Collector.Infrastructure.Remote.GitHub;

public sealed class GitHubInstallationLister(GitHubApiTransport api)
{
    private const string InstallationsPath = "user/installations?per_page=100";

    internal async Task<IReadOnlyList<RemoteRepository>> ListRepositoriesAsync(
        string? scope,
        CancellationToken cancellationToken)
    {
        var http = api.CreateHttp();
        var installations = await CollectInstallationsAsync(http, cancellationToken);
        var repositories = new List<RemoteRepository>();
        foreach (var installation in installations.Where(installation => InScope(installation, scope)))
        {
            var wire = await CollectRepositoriesAsync(http, installation.Id, cancellationToken);
            repositories.AddRange(wire.Select(GitHubRepoMapper.ToRepository).OfType<RemoteRepository>());
        }

        return repositories;
    }

    private static bool InScope(GitHubInstallationWire installation, string? scope) =>
        string.IsNullOrWhiteSpace(scope)
        || string.Equals(installation.Account?.Login, scope.Trim(), StringComparison.OrdinalIgnoreCase);

    private Task<IReadOnlyList<GitHubInstallationWire>> CollectInstallationsAsync(
        RemoteHttp http,
        CancellationToken cancellationToken)
    {
        var reader = new PageReader<GitHubInstallationsWire, GitHubInstallationWire>(
            GitHubJson.Default.GitHubInstallationsWire,
            body => body.Installations ?? []);
        return api.CreatePager().CollectAsync(
            RemoteUrl.Relative(InstallationsPath),
            (uri, token) => api.FetchPageAsync(http, uri, reader, token),
            cancellationToken);
    }

    private Task<IReadOnlyList<GitHubRepoWire>> CollectRepositoriesAsync(
        RemoteHttp http,
        long installationId,
        CancellationToken cancellationToken)
    {
        var reader = new PageReader<GitHubInstallationReposWire, GitHubRepoWire>(
            GitHubJson.Default.GitHubInstallationReposWire,
            body => body.Repositories ?? []);
        return api.CreatePager().CollectAsync(
            RemoteUrl.Relative($"user/installations/{installationId}/repositories?per_page=100"),
            (uri, token) => api.FetchPageAsync(http, uri, reader, token),
            cancellationToken);
    }
}
