using Collector.Domain.Enums;

namespace Collector.Presentation.ViewModels;

public sealed class DocumentTally
{
    private readonly List<string> _analyzedIds = [];

    public int Files { get; private set; }

    public int Analyzed { get; private set; }

    public int Failed { get; private set; }

    public int Excluded { get; private set; }

    public int Units { get; private set; }

    public int Tokens { get; private set; }

    public int PromptTokens { get; private set; }

    public int Skipped => Failed + Excluded;

    public IReadOnlyList<string> AnalyzedIds => _analyzedIds;

    public void Add(DocumentRowViewModel row) => Apply(row, 1);

    public void Remove(DocumentRowViewModel row) => Apply(row, -1);

    public void Reset()
    {
        _analyzedIds.Clear();
        Files = Analyzed = Failed = Excluded = Units = Tokens = PromptTokens = 0;
    }

    private void Apply(DocumentRowViewModel row, int sign)
    {
        Files += sign;
        Units += sign * row.UnitCount;
        Tokens += sign * row.TokenCount;
        PromptTokens += sign * row.PromptTokens;
        ApplyStatus(row, sign);
    }

    private void ApplyStatus(DocumentRowViewModel row, int sign)
    {
        switch (row.Status)
        {
            case DocumentStatus.Extracted:
                Analyzed += sign;
                TrackId(row.DocumentId, sign);
                break;
            case DocumentStatus.Failed:
                Failed += sign;
                break;
            case DocumentStatus.Excluded:
                Excluded += sign;
                break;
        }
    }

    private void TrackId(string? documentId, int sign)
    {
        if (documentId is null)
        {
            return;
        }

        if (sign > 0)
        {
            _analyzedIds.Add(documentId);
            return;
        }

        _analyzedIds.Remove(documentId);
    }
}
