using System.ComponentModel;
using Collector.Domain.Enums;
using Collector.Presentation.ViewModels;

namespace Collector.Tests.Presentation;

public sealed class DocumentSortComparerTests
{
    private static DocumentRowViewModel Row(
        string repoPath,
        int units = 0,
        int tokens = 0,
        DocumentStatus status = DocumentStatus.Extracted) =>
        new(SplitOutcomes.Make(@"C:\cache\blob.md", status, repoPath: repoPath, units: units, tokens: tokens));

    private static List<string> Sort(IEnumerable<DocumentRowViewModel> rows, DocumentSortColumn column, ListSortDirection direction)
    {
        var comparer = new DocumentSortComparer(column, direction);
        var ordered = rows.ToList();
        ordered.Sort((left, right) => comparer.Compare(left, right));
        return [.. ordered.Select(row => row.Filename)];
    }

    [Fact]
    public void Compare_NameAscending_OrdersCaseInsensitively()
    {
        var rows = new[] { Row("b.md"), Row("A.md"), Row("c.md") };

        Assert.Equal(["A.md", "b.md", "c.md"], Sort(rows, DocumentSortColumn.Name, ListSortDirection.Ascending));
    }

    [Fact]
    public void Compare_NameDescending_ReversesTheOrder()
    {
        var rows = new[] { Row("b.md"), Row("A.md"), Row("c.md") };

        Assert.Equal(["c.md", "b.md", "A.md"], Sort(rows, DocumentSortColumn.Name, ListSortDirection.Descending));
    }

    [Fact]
    public void Compare_Folder_OrdersByFolderThenName()
    {
        var rows = new[] { Row("z/a.md"), Row("a/b.md"), Row("a/a.md") };

        Assert.Equal(["a.md", "b.md", "a.md"], Sort(rows, DocumentSortColumn.Folder, ListSortDirection.Ascending));
    }

    [Fact]
    public void Compare_Units_OrdersNumerically()
    {
        var rows = new[] { Row("a.md", units: 10), Row("b.md", units: 2), Row("c.md", units: 33) };

        Assert.Equal(["b.md", "a.md", "c.md"], Sort(rows, DocumentSortColumn.Units, ListSortDirection.Ascending));
    }

    [Fact]
    public void Compare_TokensDescending_PutsTheLargestFirst()
    {
        var rows = new[] { Row("a.md", tokens: 5), Row("b.md", tokens: 500), Row("c.md", tokens: 50) };

        Assert.Equal(["b.md", "c.md", "a.md"], Sort(rows, DocumentSortColumn.Tokens, ListSortDirection.Descending));
    }

    [Fact]
    public void Compare_EqualKeys_FallBackToFilename()
    {
        var rows = new[] { Row("b.md", units: 1), Row("a.md", units: 1) };

        Assert.Equal(["a.md", "b.md"], Sort(rows, DocumentSortColumn.Units, ListSortDirection.Ascending));
    }

    [Fact]
    public void Compare_Status_OrdersByStatusValue()
    {
        var rows = new[]
        {
            Row("a.md", status: DocumentStatus.Excluded),
            Row("b.md", status: DocumentStatus.Extracted),
            Row("c.md", status: DocumentStatus.Failed),
        };

        var expected = rows.OrderBy(row => row.Status).ThenBy(row => row.Filename).Select(row => row.Filename);
        Assert.Equal(expected, Sort(rows, DocumentSortColumn.Status, ListSortDirection.Ascending));
    }

    [Fact]
    public void Compare_NonRowOperand_ReturnsZero()
    {
        var comparer = new DocumentSortComparer(DocumentSortColumn.Name, ListSortDirection.Ascending);

        Assert.Equal(0, comparer.Compare("text", Row("a.md")));
    }
}
