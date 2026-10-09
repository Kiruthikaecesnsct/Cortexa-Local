using Collector.Domain.Remote;

namespace Collector.Presentation.ViewModels;

public enum RepositoryVisibility
{
    All,
    Public,
    Private,
}

public enum RepositorySort
{
    Name,
    RecentlyUpdated,
}

public sealed record RepositoryQuery(
    string Search,
    RepositoryVisibility Visibility = RepositoryVisibility.All,
    RepositorySort Sort = RepositorySort.Name,
    bool MatchFullName = false);

public static class RepositoryFilter
{
    public static IReadOnlyList<RemoteRepository> Apply(IEnumerable<RemoteRepository> repositories, RepositoryQuery query)
    {
        var needle = query.Search.Trim();
        var visible = repositories.Where(repository => MatchesVisibility(repository, query.Visibility) && MatchesText(repository, needle, query.MatchFullName));
        return [.. Sort(visible, query.Sort)];
    }

    private static bool MatchesVisibility(RemoteRepository repository, RepositoryVisibility visibility) => visibility switch
    {
        RepositoryVisibility.Private => repository.IsPrivate,
        RepositoryVisibility.Public => !repository.IsPrivate,
        _ => true,
    };

    private static bool MatchesText(RemoteRepository repository, string needle, bool matchFullName) =>
        needle.Length == 0
        || repository.Name.Contains(needle, StringComparison.OrdinalIgnoreCase)
        || (matchFullName && repository.FullName.Contains(needle, StringComparison.OrdinalIgnoreCase))
        || (repository.Description?.Contains(needle, StringComparison.OrdinalIgnoreCase) ?? false);

    private static IEnumerable<RemoteRepository> Sort(IEnumerable<RemoteRepository> repositories, RepositorySort sort) =>
        sort == RepositorySort.Name
            ? repositories.OrderBy(repository => repository.Name, StringComparer.OrdinalIgnoreCase)
            : repositories
                .OrderBy(repository => repository.UpdatedAt is null)
                .ThenByDescending(repository => repository.UpdatedAt)
                .ThenBy(repository => repository.Name, StringComparer.OrdinalIgnoreCase);
}
