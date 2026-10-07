using Collector.Domain.Knowledge;
using Collector.Domain.Serialization;

namespace Collector.Server.Application.Building;

public static class ChunkTextRenderer
{
    private const string LineSeparator = "\n";
    private const string ExcerptLabel = "Excerpt: ";
    private const string PageContextFormat = "Structured knowledge summary from page {0}";
    private const string FileContextFormat = "Structured knowledge summary from file {0} (lines {1}-{2})";
    private const int DefaultLine = 1;

    public static string Render(KnowledgeItem item)
    {
        var lines = new List<string>();

        AddIfPresent(lines, BuildContextLine(item.Source), string.Empty);
        lines.Add($"{item.Kind.ToWire()}: {item.Title}");
        lines.Add(item.Summary);
        AddIfPresent(lines, item.Details, string.Empty);
        AddIfPresent(lines, item.Excerpt, ExcerptLabel);

        return string.Join(LineSeparator, lines);
    }

    private static string? BuildContextLine(KnowledgeSource source) =>
        source.PageNumber is { } page
            ? string.Format(PageContextFormat, page)
            : BuildFileContextLine(source);

    private static string? BuildFileContextLine(KnowledgeSource source)
    {
        if (string.IsNullOrWhiteSpace(source.FilePath))
        {
            return null;
        }

        var lineStart = source.LineStart ?? DefaultLine;
        var lineEnd = source.LineEnd ?? lineStart;
        return string.Format(FileContextFormat, source.FilePath, lineStart, lineEnd);
    }

    private static void AddIfPresent(List<string> lines, string? value, string prefix)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        lines.Add(prefix + value);
    }
}
