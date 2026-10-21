using Collector.Application.Candidates;
using Collector.Presentation.Resources;

namespace Collector.Presentation.ViewModels;

public sealed class CandidateItemViewModel
{
    private readonly InventionCandidate _candidate;

    public CandidateItemViewModel(InventionCandidate candidate)
    {
        _candidate = candidate;
    }

    public string Title => _candidate.Title;

    public string Summary => _candidate.Summary;

    public string PatentabilityRationale => _candidate.PatentabilityRationale;

    public string SourceFile => _candidate.SourceFile;

    public string Quote => _candidate.Excerpt?.Quote ?? string.Empty;

    public bool HasQuote => !string.IsNullOrWhiteSpace(Quote);

    public bool HasRange => _candidate.Excerpt is { Offset: not null, Length: not null };

    public bool HasExcerpt => HasQuote || HasRange;

    public bool IsUnverified => _candidate.Excerpt is { Verified: false };

    public string RangeText => _candidate.Excerpt is { Offset: { } start, Length: { } length }
        ? CandidateStrings.ExcerptRange(start, start + length)
        : string.Empty;

    public string AutomationName => CandidateStrings.ItemName(Title, SourceFile, IsUnverified);

    public string? HelpText => null;
}
