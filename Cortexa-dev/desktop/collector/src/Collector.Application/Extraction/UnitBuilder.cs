using Collector.Domain.Enums;

namespace Collector.Application.Extraction;

public sealed record UnitDraft
{
    public required UnitKind UnitKind { get; init; }

    public int? PageNumber { get; init; }

    public string? SectionTitle { get; init; }

    public string? FilePath { get; init; }

    public int? StartLine { get; init; }

    public int? EndLine { get; init; }

    public required string Text { get; init; }
}

public sealed class UnitBuilder(HeadingDetector headingDetector)
{
    public IReadOnlyList<UnitDraft> BuildFromPages(ParsedDocument parsed)
    {
        if (parsed.Pages.Count == 0)
        {
            return [new UnitDraft { UnitKind = UnitKind.File, Text = parsed.Text }];
        }

        var headings = headingDetector.DetectHeadings(parsed.Text);
        var drafts = new List<UnitDraft>(parsed.Pages.Count);
        foreach (var page in parsed.Pages)
        {
            var start = Math.Clamp(page.StartOffset, 0, parsed.Text.Length);
            var end = Math.Clamp(page.EndOffset, start, parsed.Text.Length);
            var pageText = parsed.Text[start..end];
            drafts.Add(new UnitDraft
            {
                UnitKind = UnitKind.Page,
                PageNumber = page.PageNumber,
                SectionTitle = headingDetector.FindHeadingForPosition(headings, start),
                Text = pageText,
            });
        }

        return drafts;
    }

    public IReadOnlyList<UnitDraft> BuildFromSections(string normalizedText)
    {
        if (string.IsNullOrEmpty(normalizedText))
        {
            return [new UnitDraft { UnitKind = UnitKind.File, Text = string.Empty }];
        }

        var headings = headingDetector.DetectHeadings(normalizedText);
        if (headings.Count == 0)
        {
            return [new UnitDraft { UnitKind = UnitKind.File, Text = normalizedText }];
        }

        var drafts = new List<UnitDraft>(headings.Count + 1);
        var preamble = normalizedText[..headings[0].StartChar].Trim();
        if (preamble.Length > 0)
        {
            drafts.Add(new UnitDraft { UnitKind = UnitKind.Section, Text = preamble });
        }

        for (var i = 0; i < headings.Count; i++)
        {
            var start = headings[i].StartChar;
            var end = i + 1 < headings.Count ? headings[i + 1].StartChar : normalizedText.Length;
            drafts.Add(new UnitDraft
            {
                UnitKind = UnitKind.Section,
                SectionTitle = headings[i].Text,
                Text = normalizedText[start..end].Trim(),
            });
        }

        return drafts;
    }

    public IReadOnlyList<UnitDraft> BuildFromCode(IReadOnlyList<CodeUnit> codeUnits, string filePath) =>
        codeUnits.Select(unit => new UnitDraft
        {
            UnitKind = UnitKind.Module,
            FilePath = filePath,
            StartLine = unit.StartLine,
            EndLine = unit.EndLine,
            Text = unit.Text,
        }).ToList();
}
