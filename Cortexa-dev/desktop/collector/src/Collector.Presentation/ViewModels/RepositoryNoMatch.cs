using Collector.Presentation.Resources;

namespace Collector.Presentation.ViewModels;

public readonly record struct RepositoryFilterState(string Search, string? Project, bool VisibilityActive)
{
    public bool HasSearch => !string.IsNullOrWhiteSpace(Search);

    public bool HasFilter => Project is not null || VisibilityActive;
}

public static class RepositoryNoMatch
{
    public static string Text(RepositoryFilterState state) => state switch
    {
        { Project: { } project, HasSearch: true } => RemoteSourceStrings.NoMatchInProjectQuery(state.Search, project),
        { Project: { } project } => RemoteSourceStrings.NoMatchInProject(project),
        { HasSearch: true } => RemoteSourceStrings.NoMatch(state.Search),
        _ => RemoteSourceStrings.NoMatchFiltered(),
    };

    public static string ActionLabel(RepositoryFilterState state) =>
        state.HasFilter ? RemoteSourceStrings.ClearFilters : RemoteSourceStrings.ClearSearch;

    public static string ActionName(RepositoryFilterState state) =>
        state.HasFilter ? RemoteSourceStrings.ClearFiltersName : RemoteSourceStrings.ClearSearch;
}
