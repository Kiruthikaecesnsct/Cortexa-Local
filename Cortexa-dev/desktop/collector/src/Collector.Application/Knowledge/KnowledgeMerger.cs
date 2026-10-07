using System.Text.RegularExpressions;
using Collector.Domain.Knowledge;

namespace Collector.Application.Knowledge;

public sealed partial class KnowledgeMerger
{
    private const string DetailsSeparator = "\n\n";

    public IReadOnlyList<ExtractedKnowledgeItem> Merge(IEnumerable<ExtractedKnowledgeItem> items) =>
        [.. items.GroupBy(GroupKey).Select(MergeGroup)];

    private static (string DocumentId, int Kind, string Title) GroupKey(ExtractedKnowledgeItem item) =>
        (item.DocumentId, (int)item.Kind, Normalize(item.Title));

    private static string Normalize(string title) =>
        WhitespaceRun().Replace(title.Trim(), " ").ToLowerInvariant();

    private static ExtractedKnowledgeItem MergeGroup(IGrouping<(string DocumentId, int Kind, string Title), ExtractedKnowledgeItem> group)
    {
        var members = group.ToList();
        var first = members[0];
        var best = members.MaxBy(member => member.Summary.Length)!;
        return first with
        {
            Summary = best.Summary,
            EchoVerdict = best.EchoVerdict,
            Details = JoinDetails(members),
            Source = MergeSource(members),
            Excerpt = members.Select(member => member.Excerpt).FirstOrDefault(excerpt => excerpt is not null),
        };
    }

    private static string? JoinDetails(List<ExtractedKnowledgeItem> members)
    {
        var distinct = members
            .Select(member => member.Details)
            .Where(details => !string.IsNullOrWhiteSpace(details))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        return distinct.Count == 0
            ? null
            : UploadFieldClamp.Truncate(string.Join(DetailsSeparator, distinct), UploadLimitsMirror.DetailsMax);
    }

    private static KnowledgeSource MergeSource(List<ExtractedKnowledgeItem> members)
    {
        var first = members[0].Source;
        var sameFile = members.Where(member => member.Source.FilePath == first.FilePath && member.Source.LineStart is not null).ToList();
        if (first.FilePath is null || sameFile.Count == 0)
        {
            return first;
        }

        return first with
        {
            LineStart = sameFile.Min(member => member.Source.LineStart),
            LineEnd = sameFile.Max(member => member.Source.LineEnd),
        };
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRun();
}
