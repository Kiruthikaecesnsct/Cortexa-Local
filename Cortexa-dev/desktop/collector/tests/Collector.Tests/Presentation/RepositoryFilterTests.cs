using Collector.Domain.Enums;
using Collector.Domain.Remote;
using Collector.Presentation.ViewModels;

namespace Collector.Tests.Presentation;

public sealed class RepositoryFilterTests
{
    private static readonly DateTimeOffset Base = new(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);

    private static RemoteRepository Repo(
        string name,
        bool isPrivate = false,
        string? description = null,
        DateTimeOffset? updated = null) =>
        new(SourceType.Github, "octo", null, name, $"octo/{name}", "main", string.Empty, 0, isPrivate, description, updated);

    private static readonly RemoteRepository[] Sample =
    [
        Repo("gamma", isPrivate: true, description: "Billing service", updated: Base.AddDays(-1)),
        Repo("Alpha", description: "Docs site", updated: Base.AddDays(-10)),
        Repo("beta", isPrivate: true, description: null, updated: null),
        Repo("delta", description: "billing tools", updated: Base.AddDays(-5)),
    ];

    private static string[] Names(IEnumerable<RemoteRepository> repositories) => [.. repositories.Select(repository => repository.Name)];

    [Fact]
    public void Apply_DefaultQuery_SortsByNameIgnoringCase()
    {
        var result = RepositoryFilter.Apply(Sample, new RepositoryQuery(string.Empty));

        Assert.Equal(["Alpha", "beta", "delta", "gamma"], Names(result));
    }

    [Theory]
    [InlineData("ALPHA", new[] { "Alpha" })]
    [InlineData("  bet ", new[] { "beta" })]
    [InlineData("billing", new[] { "delta", "gamma" })]
    [InlineData("docs", new[] { "Alpha" })]
    [InlineData("zzz", new string[0])]
    public void Apply_Search_MatchesNameAndDescriptionCaseInsensitively(string search, string[] expected)
    {
        var result = RepositoryFilter.Apply(Sample, new RepositoryQuery(search));

        Assert.Equal(expected, Names(result));
    }

    [Fact]
    public void Apply_SearchDoesNotMatchTheOwner()
    {
        var result = RepositoryFilter.Apply(Sample, new RepositoryQuery("octo"));

        Assert.Empty(result);
    }

    [Fact]
    public void Apply_MatchFullName_AlsoMatchesTheOwner()
    {
        var result = RepositoryFilter.Apply(Sample, new RepositoryQuery("octo", MatchFullName: true));

        Assert.Equal(4, result.Count);
    }

    [Theory]
    [InlineData(RepositoryVisibility.All, new[] { "Alpha", "beta", "delta", "gamma" })]
    [InlineData(RepositoryVisibility.Public, new[] { "Alpha", "delta" })]
    [InlineData(RepositoryVisibility.Private, new[] { "beta", "gamma" })]
    public void Apply_Visibility_FiltersByPrivacy(RepositoryVisibility visibility, string[] expected)
    {
        var result = RepositoryFilter.Apply(Sample, new RepositoryQuery(string.Empty, visibility));

        Assert.Equal(expected, Names(result));
    }

    [Fact]
    public void Apply_RecentlyUpdated_PutsNewestFirstAndUnknownDatesLast()
    {
        var result = RepositoryFilter.Apply(Sample, new RepositoryQuery(string.Empty, Sort: RepositorySort.RecentlyUpdated));

        Assert.Equal(["gamma", "delta", "Alpha", "beta"], Names(result));
    }

    [Fact]
    public void Apply_RecentlyUpdatedWithSameDate_FallsBackToName()
    {
        RemoteRepository[] repositories = [Repo("b", updated: Base), Repo("a", updated: Base)];

        var result = RepositoryFilter.Apply(repositories, new RepositoryQuery(string.Empty, Sort: RepositorySort.RecentlyUpdated));

        Assert.Equal(["a", "b"], Names(result));
    }

    [Fact]
    public void Apply_PrivateSearchAndRecentSort_Combine()
    {
        var result = RepositoryFilter.Apply(
            Sample,
            new RepositoryQuery("billing", RepositoryVisibility.Private, RepositorySort.RecentlyUpdated));

        Assert.Equal(["gamma"], Names(result));
    }

    [Fact]
    public void Apply_DoesNotChangeTheSource()
    {
        var before = Names(Sample);

        RepositoryFilter.Apply(Sample, new RepositoryQuery("a", RepositoryVisibility.Public, RepositorySort.RecentlyUpdated));

        Assert.Equal(before, Names(Sample));
    }
}
