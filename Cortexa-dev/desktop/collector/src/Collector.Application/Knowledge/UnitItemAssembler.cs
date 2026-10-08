using Collector.Domain.Documents;
using Collector.Domain.Enums;
using Collector.Domain.Extraction;
using Collector.Domain.Knowledge;

namespace Collector.Application.Knowledge;

public sealed class UnitItemAssembler(
    AnchorLocator anchorLocator,
    ExcerptCutter excerptCutter,
    IdentifierEchoDetector echoDetector,
    KnowledgePrompt prompt)
{
    public ExtractedKnowledgeItem Assemble(ExtractionUnit unit, CollectorDocument document, RawKnowledgeItem raw)
    {
        var anchor = anchorLocator.Locate(unit, raw.AnchorQuote);
        return new ExtractedKnowledgeItem
        {
            DocumentId = document.Id,
            DocumentName = document.Filename,
            DocumentPath = document.SourcePath,
            UnitKind = unit.UnitKind,
            Kind = raw.Kind,
            Title = UploadFieldClamp.Truncate(raw.Title, UploadLimitsMirror.TitleMax),
            Summary = UploadFieldClamp.Truncate(raw.Summary, UploadLimitsMirror.SummaryMax),
            Details = UploadFieldClamp.TruncateOptional(raw.Details, UploadLimitsMirror.DetailsMax),
            Source = BuildSource(unit, anchor),
            Excerpt = excerptCutter.Cut(unit.Text, anchor),
            EchoVerdict = echoDetector.Evaluate(raw.Title, raw.Summary, unit.Text),
            PromptVersion = prompt.Version,
        };
    }

    private static KnowledgeSource BuildSource(ExtractionUnit unit, Anchor? anchor) => unit.UnitKind switch
    {
        UnitKind.File => new KnowledgeSource
        {
            FilePath = unit.FilePath,
            LineStart = anchor?.LineStart ?? unit.StartLine,
            LineEnd = anchor?.LineEnd ?? unit.EndLine,
        },
        _ => new KnowledgeSource { PageNumber = unit.PageNumber, Section = unit.SectionTitle },
    };
}
