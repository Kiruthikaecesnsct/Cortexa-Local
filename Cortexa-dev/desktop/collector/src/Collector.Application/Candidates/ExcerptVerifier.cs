using Collector.Application.Knowledge;
using Collector.Domain.Enums;
using Collector.Domain.Extraction;

namespace Collector.Application.Candidates;

public sealed class ExcerptVerifier(AnchorLocator anchorLocator)
{
    public IReadOnlyList<InventionCandidate> Verify(WholeDocument document, IReadOnlyList<InventionCandidate> candidates)
    {
        var unit = ToUnit(document);
        return [.. candidates.Select(candidate => VerifyOne(unit, candidate))];
    }

    private InventionCandidate VerifyOne(ExtractionUnit unit, InventionCandidate candidate)
    {
        if (candidate.Excerpt is null)
        {
            return candidate;
        }

        var anchor = anchorLocator.Locate(unit, candidate.Excerpt.Quote);
        var excerpt = anchor is null
            ? candidate.Excerpt with { Verified = false, Offset = null, Length = null }
            : candidate.Excerpt with { Verified = true, Offset = anchor.Offset, Length = anchor.Length };
        return candidate with { Excerpt = excerpt };
    }

    private static ExtractionUnit ToUnit(WholeDocument document) => new()
    {
        Id = document.DocumentId,
        DocumentId = document.DocumentId,
        Ordinal = 0,
        UnitKind = document.IsCode ? UnitKind.File : UnitKind.Section,
        StartLine = null,
        EndLine = null,
        Text = document.Text,
        TokenCount = 0,
        Status = DocumentStatus.Extracted,
    };
}
