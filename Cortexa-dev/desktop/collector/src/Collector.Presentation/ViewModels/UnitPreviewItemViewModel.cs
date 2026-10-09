using Collector.Domain.Enums;
using Collector.Domain.Extraction;
using Collector.Presentation.Resources;

namespace Collector.Presentation.ViewModels;

public sealed class UnitPreviewItemViewModel
{
    private const int SnippetMaxLength = 320;

    public UnitPreviewItemViewModel(ExtractionUnit unit, string documentSourcePath)
    {
        Ordinal = unit.Ordinal;
        UnitKind = unit.UnitKind;
        KindLabel = KindLabelFor(unit.UnitKind);
        BoundaryLabel = BoundaryLabelFor(unit, documentSourcePath);
        Title = BoundaryLabel;
        Snippet = Truncate(unit.Text);
        FullText = unit.Text;
        TokenCount = unit.TokenCount;
    }

    public int Ordinal { get; }

    public UnitKind UnitKind { get; }

    public string KindLabel { get; }

    public string BoundaryLabel { get; }

    public string Title { get; }

    public string Snippet { get; }

    public string FullText { get; }

    public int TokenCount { get; }

    public string AutomationName =>
        ExtractionStrings.SectionAutomationName(Ordinal, KindLabel, Title, TokenCount);

    public static string KindLabelFor(UnitKind kind) => kind switch
    {
        UnitKind.Page => ExtractionStrings.KindPage,
        UnitKind.Section => ExtractionStrings.KindSection,
        UnitKind.File => ExtractionStrings.KindFile,
        UnitKind.Module => ExtractionStrings.KindModule,
        _ => ExtractionStrings.KindFile,
    };

    public static string BoundaryLabelFor(ExtractionUnit unit, string documentSourcePath) => unit.UnitKind switch
    {
        UnitKind.Page => $"{ExtractionStrings.KindPage} {unit.PageNumber}",
        UnitKind.Section => unit.SectionTitle ?? ExtractionStrings.KindSection,
        UnitKind.File => FileBoundaryLabel(unit, documentSourcePath),
        UnitKind.Module => ModuleBoundaryLabel(unit),
        _ => documentSourcePath,
    };

    private static string FileBoundaryLabel(ExtractionUnit unit, string documentSourcePath)
    {
        var path = unit.FilePath ?? documentSourcePath;
        if (unit.StartLine is not { } start || unit.EndLine is not { } end)
        {
            return path;
        }

        return $"{path} · lines {start}-{end}{ExtractionStrings.WindowSuffix}";
    }

    private static string ModuleBoundaryLabel(ExtractionUnit unit)
    {
        var lines = $"lines {unit.StartLine}-{unit.EndLine}";
        return string.IsNullOrWhiteSpace(unit.SectionTitle) ? lines : $"{unit.SectionTitle} · {lines}";
    }

    private static string Truncate(string text)
    {
        var trimmed = text.Trim();
        return trimmed.Length <= SnippetMaxLength ? trimmed : trimmed[..SnippetMaxLength] + "…";
    }
}
