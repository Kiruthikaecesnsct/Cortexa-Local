using System.Text.Json;

namespace Collector.Application.Candidates;

public sealed record CandidateParseResult
{
    public IReadOnlyList<InventionCandidate>? Candidates { get; private init; }

    public string? Failure { get; private init; }

    public bool IsSuccess => Candidates is not null;

    public static CandidateParseResult Success(IReadOnlyList<InventionCandidate> candidates) => new() { Candidates = candidates };

    public static CandidateParseResult Failed(string reason) => new() { Failure = reason };
}

public sealed class CandidateParser
{
    public const int MaxTitleWords = 12;
    public const int MaxSummaryWords = 60;
    public const int MaxRationaleWords = 60;
    public const int MaxQuoteChars = 200;

    private const string MalformedReason = "output malformed";
    private const string CandidatesProperty = "candidates";

    public CandidateParseResult Parse(string text, string sourceFile)
    {
        try
        {
            using var document = JsonDocument.Parse(text);
            return ParseRoot(document.RootElement, sourceFile);
        }
        catch (JsonException)
        {
            return CandidateParseResult.Failed(MalformedReason);
        }
    }

    private static CandidateParseResult ParseRoot(JsonElement root, string sourceFile)
    {
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty(CandidatesProperty, out var array)
            || array.ValueKind != JsonValueKind.Array)
        {
            return CandidateParseResult.Failed(MalformedReason);
        }

        var candidates = array.EnumerateArray()
            .Select(item => ParseItem(item, sourceFile))
            .OfType<InventionCandidate>()
            .ToList();
        return candidates.Count == 0 && array.GetArrayLength() > 0
            ? CandidateParseResult.Failed(MalformedReason)
            : CandidateParseResult.Success(candidates);
    }

    private static InventionCandidate? ParseItem(JsonElement item, string sourceFile)
    {
        if (item.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var title = RequiredText(item, "title");
        var summary = RequiredText(item, "summary");
        var rationale = RequiredText(item, "patentability_rationale");
        if (title is null || summary is null || rationale is null)
        {
            return null;
        }

        return new InventionCandidate(
            ClampWords(title, MaxTitleWords),
            ClampWords(summary, MaxSummaryWords),
            ClampWords(rationale, MaxRationaleWords),
            sourceFile,
            ReadExcerpt(item));
    }

    private static string? RequiredText(JsonElement item, string name)
    {
        if (!item.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        var text = value.GetString()?.Trim();
        return string.IsNullOrEmpty(text) ? null : text;
    }

    private static CandidateExcerpt? ReadExcerpt(JsonElement item)
    {
        if (!item.TryGetProperty("excerpt_quote", out var value) || value.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        var quote = value.GetString()?.Trim();
        if (string.IsNullOrEmpty(quote))
        {
            return null;
        }

        var clamped = quote.Length > MaxQuoteChars ? quote[..MaxQuoteChars].TrimEnd() : quote;
        return new CandidateExcerpt(clamped, false, null, null);
    }

    private static string ClampWords(string text, int maxWords)
    {
        var words = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        return words.Length <= maxWords ? text : string.Join(' ', words.Take(maxWords));
    }
}
