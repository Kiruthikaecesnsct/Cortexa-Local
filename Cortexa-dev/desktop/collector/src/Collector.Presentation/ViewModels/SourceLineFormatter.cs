using Collector.Domain.Knowledge;
using Collector.Presentation.Resources;

namespace Collector.Presentation.ViewModels;

public static class SourceLineFormatter
{
    public static bool IsFileSource(KnowledgeSource source) => !string.IsNullOrWhiteSpace(source.FilePath);

    public static string Format(KnowledgeSource source, string fallback)
    {
        if (IsFileSource(source))
        {
            return FileLine(source.FilePath!, source);
        }

        var parts = new List<string>(2);
        if (source.PageNumber is { } page)
        {
            parts.Add(ReviewStrings.SourcePage(page));
        }

        if (!string.IsNullOrWhiteSpace(source.Section))
        {
            parts.Add(source.Section);
        }

        return parts.Count > 0 ? string.Join(ReviewStrings.SourceJoin, parts) : fallback;
    }

    private static string FileLine(string path, KnowledgeSource source) =>
        source.LineStart is { } first ? ReviewStrings.SourceLines(path, first, source.LineEnd ?? first) : path;
}
