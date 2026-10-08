using Collector.Application.Knowledge;
using Collector.Domain.Enums;
using Collector.Domain.Knowledge;
using Collector.Tests.Support;

namespace Collector.Tests.Knowledge;

public sealed class FolderGroupingTests
{
    private static ExtractedKnowledgeItem FileItem(string filePath, UnitKind unitKind = UnitKind.File) =>
        TestData.Item() with
        {
            UnitKind = unitKind,
            Source = new KnowledgeSource { FilePath = filePath, LineStart = 1, LineEnd = 2 },
        };

    [Fact]
    public void Group_FolderWithThreeOrMoreFiles_IsIncluded()
    {
        var items = new[]
        {
            FileItem("src/app/a.cs"),
            FileItem("src/app/b.cs"),
            FileItem("src/app/c.cs"),
        };

        var groups = FolderGrouping.Group(items);

        var group = Assert.Single(groups);
        Assert.Equal("src/app", group.FolderPath);
        Assert.Equal(3, group.Files.Count);
    }

    [Fact]
    public void Group_FolderWithFewerThanThreeFiles_IsExcluded()
    {
        var items = new[]
        {
            FileItem("src/app/a.cs"),
            FileItem("src/app/b.cs"),
        };

        var groups = FolderGrouping.Group(items);

        Assert.Empty(groups);
    }

    [Fact]
    public void Group_NonFileUnitKind_IsIgnored()
    {
        var items = new[]
        {
            FileItem("src/app/a.cs", UnitKind.Page),
            FileItem("src/app/b.cs", UnitKind.Page),
            FileItem("src/app/c.cs", UnitKind.Page),
        };

        var groups = FolderGrouping.Group(items);

        Assert.Empty(groups);
    }

    [Fact]
    public void Group_SameFileMultipleItems_CountsAsOneFile()
    {
        var items = new[]
        {
            FileItem("src/app/a.cs"),
            FileItem("src/app/a.cs"),
            FileItem("src/app/b.cs"),
            FileItem("src/app/c.cs"),
        };

        var groups = FolderGrouping.Group(items);

        var group = Assert.Single(groups);
        Assert.Equal(3, group.Files.Count);
        Assert.Equal(2, group.Files.Single(file => file.FilePath == "src/app/a.cs").Items.Count);
    }

    [Fact]
    public void Group_RepresentativeFile_IsFirstFileByAppearanceOrder()
    {
        var items = new[]
        {
            FileItem("src/app/c.cs") with { DocumentId = "doc-c" },
            FileItem("src/app/a.cs") with { DocumentId = "doc-a" },
            FileItem("src/app/b.cs") with { DocumentId = "doc-b" },
        };

        var group = Assert.Single(FolderGrouping.Group(items));

        Assert.Equal("doc-c", group.RepresentativeFile.RepresentativeItem.DocumentId);
    }

    [Fact]
    public void Group_DistinctFolders_ProduceSeparateGroups()
    {
        var items = new[]
        {
            FileItem("src/app/a.cs"),
            FileItem("src/app/b.cs"),
            FileItem("src/app/c.cs"),
            FileItem("src/infra/x.cs"),
            FileItem("src/infra/y.cs"),
            FileItem("src/infra/z.cs"),
        };

        var groups = FolderGrouping.Group(items);

        Assert.Equal(["src/app", "src/infra"], groups.Select(group => group.FolderPath).Order());
    }

    [Theory]
    [InlineData("src/app/a.cs", "src/app")]
    [InlineData("a.cs", null)]
    [InlineData(@"src\app\a.cs", "src/app")]
    public void ParentFolder_ReturnsNormalizedParentOrNull(string filePath, string? expected)
    {
        Assert.Equal(expected, FolderGrouping.ParentFolder(filePath));
    }
}
