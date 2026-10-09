using Collector.Domain.Enums;

namespace Collector.Presentation.ViewModels;

public static class DocumentFilter
{
    public static bool Matches(DocumentRowViewModel row, DocumentStatusFilter status, string search) =>
        MatchesStatus(row, status) && MatchesSearch(row, search);

    private static bool MatchesStatus(DocumentRowViewModel row, DocumentStatusFilter status) => status switch
    {
        DocumentStatusFilter.Analyzed => row.Status == DocumentStatus.Extracted,
        DocumentStatusFilter.Failed => row.Status == DocumentStatus.Failed,
        DocumentStatusFilter.Skipped => row.Status == DocumentStatus.Excluded,
        _ => true,
    };

    private static bool MatchesSearch(DocumentRowViewModel row, string search)
    {
        var term = search.Trim();
        if (term.Length == 0)
        {
            return true;
        }

        return row.Filename.Contains(term, StringComparison.OrdinalIgnoreCase)
            || row.Folder.Contains(term, StringComparison.OrdinalIgnoreCase);
    }
}
