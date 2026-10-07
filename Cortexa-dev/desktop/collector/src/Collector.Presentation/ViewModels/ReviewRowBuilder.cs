using Collector.Application.Knowledge;

namespace Collector.Presentation.ViewModels;

public static class ReviewRowBuilder
{
    public static IReadOnlyList<DocumentHeaderRowViewModel> BuildGroups(IReadOnlyList<ExtractedKnowledgeItem> items) =>
    [
        .. items
            .GroupBy(item => item.DocumentId)
            .Select(group => new DocumentHeaderRowViewModel(
                group.Key,
                group.First().DocumentName,
                group.First().DocumentPath,
                [.. group.Select(item => new KnowledgeItemRowViewModel(item))])),
    ];

    public static IReadOnlyList<object> Flatten(IReadOnlyList<DocumentHeaderRowViewModel> groups, KindFilterViewModel filter)
    {
        var rows = new List<object>();
        foreach (var group in groups)
        {
            var visible = group.Rows.Where(filter.Matches).ToList();
            group.ApplyFilter(visible, filter.Kind is not null);
            if (visible.Count == 0)
            {
                continue;
            }

            rows.Add(group);
            rows.AddRange(visible);
        }

        return rows;
    }
}
