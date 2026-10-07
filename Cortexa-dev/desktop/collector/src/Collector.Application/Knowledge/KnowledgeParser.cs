using System.Text.Json;
using Collector.Domain.Enums;
using Collector.Domain.Serialization;

namespace Collector.Application.Knowledge;

public sealed class KnowledgeParser
{
    private const string ItemsProperty = "items";
    private static readonly char[] FenceTrim = ['`', ' ', '\r', '\n', '\t'];

    private static readonly IReadOnlyDictionary<string, KnowledgeKind> KindsByWire = Enum
        .GetValues<KnowledgeKind>()
        .Where(kind => kind != KnowledgeKind.Layer)
        .ToDictionary(kind => kind.ToWire(), StringComparer.Ordinal);

    public KnowledgeParseResult Parse(string modelText)
    {
        using var document = TryOpen(modelText);
        if (document is null
            || document.RootElement.ValueKind != JsonValueKind.Object
            || !document.RootElement.TryGetProperty(ItemsProperty, out var items)
            || items.ValueKind != JsonValueKind.Array)
        {
            return KnowledgeParseResult.Failure;
        }

        var parsed = items.EnumerateArray().Select(ToItem).ToList();
        var kept = parsed.OfType<RawKnowledgeItem>().ToList();
        return new KnowledgeParseResult(true, kept, parsed.Count - kept.Count);
    }

    private static JsonDocument? TryOpen(string text)
    {
        foreach (var candidate in Candidates(text))
        {
            try
            {
                return JsonDocument.Parse(candidate);
            }
            catch (JsonException)
            {
            }
        }

        return null;
    }

    private static IEnumerable<string> Candidates(string text)
    {
        var trimmed = text.Trim();
        yield return trimmed;
        yield return StripFence(trimmed);
        var open = trimmed.IndexOf('{');
        var close = trimmed.LastIndexOf('}');
        if (open >= 0 && close > open)
        {
            yield return trimmed[open..(close + 1)];
        }
    }

    private static string StripFence(string text)
    {
        if (!text.StartsWith("```", StringComparison.Ordinal))
        {
            return text;
        }

        var firstBreak = text.IndexOf('\n');
        return firstBreak < 0 ? text : text[(firstBreak + 1)..].TrimEnd(FenceTrim);
    }

    private static RawKnowledgeItem? ToItem(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object
            || !KindsByWire.TryGetValue(ReadString(element, "kind") ?? string.Empty, out var kind))
        {
            return null;
        }

        var title = ReadString(element, "title")?.Trim();
        var summary = ReadString(element, "summary")?.Trim();
        if (string.IsNullOrEmpty(title) || string.IsNullOrEmpty(summary))
        {
            return null;
        }

        return new RawKnowledgeItem
        {
            Kind = kind,
            Title = title,
            Summary = summary,
            Details = NullIfBlank(ReadString(element, "details")),
            AnchorQuote = NullIfBlank(ReadString(element, "anchor_quote")),
        };
    }

    private static string? ReadString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
