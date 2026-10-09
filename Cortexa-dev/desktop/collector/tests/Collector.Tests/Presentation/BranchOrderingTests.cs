using Collector.Domain.Remote;
using Collector.Presentation.ViewModels;

namespace Collector.Tests.Presentation;

public sealed class BranchOrderingTests
{
    private static readonly RemoteBranch[] Branches =
    [
        new("release", "r1"),
        new("main", "m1"),
        new("Develop", "d1"),
        new("feature/login", "f1", IsProtected: true),
    ];

    private static string[] Names(IEnumerable<RemoteBranch> branches) => [.. branches.Select(branch => branch.Name)];

    [Fact]
    public void Order_PutsTheDefaultBranchFirstThenTheRestByName()
    {
        var result = BranchOrdering.Order(Branches, "main", string.Empty);

        Assert.Equal(["main", "Develop", "feature/login", "release"], Names(result));
    }

    [Fact]
    public void Order_DefaultBranchMissing_JustSortsByName()
    {
        var result = BranchOrdering.Order(Branches, "trunk", string.Empty);

        Assert.Equal(["Develop", "feature/login", "main", "release"], Names(result));
    }

    [Theory]
    [InlineData("FEAT", new[] { "feature/login" })]
    [InlineData("  e ", new[] { "Develop", "feature/login", "release" })]
    [InlineData("nothing", new string[0])]
    public void Order_Search_FiltersByNameCaseInsensitively(string search, string[] expected)
    {
        var result = BranchOrdering.Order(Branches, "main", search);

        Assert.Equal(expected, Names(result));
    }

    [Fact]
    public void Order_SearchStillKeepsTheDefaultFirst()
    {
        var result = BranchOrdering.Order(Branches, "release", "e");

        Assert.Equal("release", result[0].Name);
    }

    [Fact]
    public void Order_KeepsTheProtectedFlag()
    {
        var result = BranchOrdering.Order(Branches, "main", "feature");

        Assert.True(Assert.Single(result).IsProtected);
    }

    [Theory]
    [InlineData("3f2a9c1e55aa", "3f2a9c1")]
    [InlineData("abc", "abc")]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void ShortSha_UsesAtMostSevenCharacters(string? sha, string? expected) =>
        Assert.Equal(expected, BranchOrdering.ShortSha(sha));
}
