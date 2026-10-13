using Collector.Domain.Enums;
using Collector.Domain.Remote;

namespace Collector.Infrastructure.Remote.GitHub;

internal static class GitHubRepoMapper
{
    private const string FallbackBranch = "main";
    private const long BytesPerKilobyte = 1024;

    public static RemoteRepository? ToRepository(GitHubRepoWire wire)
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
            wire.IsPrivate,
            wire.Description,
            wire.UpdatedAt);
    }
}
