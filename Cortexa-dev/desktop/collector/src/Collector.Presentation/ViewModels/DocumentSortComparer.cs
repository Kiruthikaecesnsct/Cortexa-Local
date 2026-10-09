using System.Collections;
using System.ComponentModel;

namespace Collector.Presentation.ViewModels;

public sealed class DocumentSortComparer(DocumentSortColumn column, ListSortDirection direction) : IComparer
{
    public int Compare(object? x, object? y)
    {
        if (x is not DocumentRowViewModel left || y is not DocumentRowViewModel right)
        {
            return 0;
        }

        var result = CompareColumn(left, right);
        if (result == 0)
        {
            result = StringComparer.OrdinalIgnoreCase.Compare(left.Filename, right.Filename);
        }

        return direction == ListSortDirection.Ascending ? result : -result;
    }

    private int CompareColumn(DocumentRowViewModel left, DocumentRowViewModel right) => column switch
    {
        DocumentSortColumn.Folder => StringComparer.OrdinalIgnoreCase.Compare(left.Folder, right.Folder),
        DocumentSortColumn.Units => left.UnitCount.CompareTo(right.UnitCount),
        DocumentSortColumn.Tokens => left.TokenCount.CompareTo(right.TokenCount),
        DocumentSortColumn.Status => left.Status.CompareTo(right.Status),
        _ => StringComparer.OrdinalIgnoreCase.Compare(left.Filename, right.Filename),
    };
}
