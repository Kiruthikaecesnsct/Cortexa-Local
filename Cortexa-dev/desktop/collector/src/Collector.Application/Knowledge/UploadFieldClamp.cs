using Collector.Domain.Knowledge;

namespace Collector.Application.Knowledge;

public static class UploadFieldClamp
{
    private const int MinWordBoundaryDivisor = 2;
    private const int MinPosition = 1;
    private const string FallbackSection = "Untitled section";

    public static string Truncate(string text, int max)
    {
        var trimmed = text.Trim();
        if (trimmed.Length <= max)
        {
            return trimmed;
        }

        var cut = trimmed[..max];
        var boundary = cut.LastIndexOfAny([' ', '\n', '\t', '\r']);
        return (boundary > max / MinWordBoundaryDivisor ? cut[..boundary] : cut).TrimEnd();
    }

    public static string? TruncateOptional(string? text, int max) =>
        string.IsNullOrWhiteSpace(text) ? null : Truncate(text, max);

    public static KnowledgeItem ToKnowledgeItem(ExtractedKnowledgeItem item) => new()
    {
        Kind = item.Kind,
        UnitKind = item.UnitKind,
        Title = Truncate(item.Title, UploadLimitsMirror.TitleMax),
        Summary = Truncate(item.Summary, UploadLimitsMirror.SummaryMax),
        Details = TruncateOptional(item.Details, UploadLimitsMirror.DetailsMax),
        Excerpt = TruncateOptional(item.Excerpt, UploadLimitsMirror.ExcerptMax),
        Source = ClampSource(item),
    };

    private static KnowledgeSource ClampSource(ExtractedKnowledgeItem item)
    {
        var source = item.Source;
        return item.UnitKind switch
        {
            Domain.Enums.UnitKind.Page => new KnowledgeSource
            {
                PageNumber = Math.Max(source.PageNumber ?? MinPosition, MinPosition),
                Section = TruncateOptional(source.Section, UploadLimitsMirror.SectionMax),
            },
            Domain.Enums.UnitKind.Section => new KnowledgeSource
            {
                PageNumber = source.PageNumber is >= MinPosition ? source.PageNumber : null,
                Section = TruncateOptional(source.Section, UploadLimitsMirror.SectionMax) ?? FallbackSection,
            },
            Domain.Enums.UnitKind.Module => ClampModuleSource(item),
            _ => ClampFileSource(item),
        };
    }

    private static KnowledgeSource ClampModuleSource(ExtractedKnowledgeItem item) => new()
    {
        FilePath = Truncate(item.Source.FilePath ?? item.DocumentPath, UploadLimitsMirror.FilePathMax),
        LineStart = null,
        LineEnd = null,
    };

    private static KnowledgeSource ClampFileSource(ExtractedKnowledgeItem item)
    {
        var source = item.Source;
        var start = source.LineStart is >= MinPosition ? source.LineStart : null;
        var end = source.LineEnd is >= MinPosition ? source.LineEnd : null;
        return new KnowledgeSource
        {
            FilePath = Truncate(source.FilePath ?? item.DocumentPath, UploadLimitsMirror.FilePathMax),
            LineStart = start,
            LineEnd = start is not null && end is not null && end < start ? start : end,
        };
    }
}
