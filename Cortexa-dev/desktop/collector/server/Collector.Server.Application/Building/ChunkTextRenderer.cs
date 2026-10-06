using Collector.Domain.Knowledge;
using Collector.Domain.Serialization;

namespace Collector.Server.Application.Building;

public static class ChunkTextRenderer
{
    private const string LineSeparator = "\n";
    private const string ExcerptLabel = "Excerpt: ";

    public static string Render(KnowledgeItem item)
    {
        var lines = new List<string>
        {
            $"{item.Kind.ToWire()}: {item.Title}",
            item.Summary
        };

        AddIfPresent(lines, item.Details, string.Empty);
        AddIfPresent(lines, item.Excerpt, ExcerptLabel);

        return string.Join(LineSeparator, lines);
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
