using System.Collections.ObjectModel;
using Collector.Domain.History;
using Collector.Presentation.Resources;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Collector.Presentation.ViewModels;

public sealed partial class MetricCellViewModel : ObservableObject
{
    public MetricCellViewModel(string label, string nullTooltip)
    {
        Label = label;
        NullTooltip = nullTooltip;
        Text = HistoryStrings.NullMetric;
        IsNull = true;
    }

    public string Label { get; }

    public string NullTooltip { get; }

    public string? ToolTip => IsNull ? NullTooltip : null;

    [ObservableProperty]
    public partial string Text { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ToolTip))]
    public partial bool IsNull { get; set; }

    public void Set(string? value)
    {
        IsNull = value is null;
        Text = value ?? HistoryStrings.NullMetric;
    }
}

public sealed partial class CandidateRowViewModel : ObservableObject
{
    private readonly HistorySourceContext _context;
    private readonly KeyedMerge<KnowledgeLinkRowViewModel, CandidateKnowledgeLink> _linkMerge;
    private readonly MetricCellViewModel _score = new(HistoryStrings.LabelScore, HistoryStrings.NotScored);
    private readonly MetricCellViewModel _patentability = new(HistoryStrings.LabelPatentability, HistoryStrings.NotRated);
    private readonly MetricCellViewModel _evidence = new(HistoryStrings.LabelEvidence, string.Empty);

    public CandidateRowViewModel(BatchCandidate candidate, HistorySourceContext context)
    {
        _context = context;
        CandidateId = candidate.CandidateId;
        _linkMerge = new KeyedMerge<KnowledgeLinkRowViewModel, CandidateKnowledgeLink>(
            row => row.Key,
            KnowledgeLinkRowViewModel.KeyOf,
            link => new KnowledgeLinkRowViewModel(link, _context),
            (row, link) => row.Update(link));
        Links = [];
        Metrics = [_score, _patentability, _evidence];
        Title = string.Empty;
        EngineLabel = string.Empty;
        KindLabel = string.Empty;
        LinkedText = string.Empty;
        AutomationName = string.Empty;
        Update(candidate);
    }

    public string CandidateId { get; }

    public ObservableCollection<KnowledgeLinkRowViewModel> Links { get; }

    public IReadOnlyList<MetricCellViewModel> Metrics { get; }

    public bool IsHarvesting { get; private set; }

    public bool HasLinks => Links.Count > 0;

    public bool HasNoLinks => Links.Count == 0;

    [ObservableProperty]
    public partial string Title { get; set; }

    [ObservableProperty]
    public partial string EngineLabel { get; set; }

    [ObservableProperty]
    public partial string KindLabel { get; set; }

    [ObservableProperty]
    public partial string LinkedText { get; set; }

    [ObservableProperty]
    public partial string AutomationName { get; set; }

    [ObservableProperty]
    public partial bool IsExpanded { get; set; }

    public void Update(BatchCandidate candidate)
    {
        IsHarvesting = CandidateFormat.IsHarvesting(candidate.Engine);
        Title = candidate.Title;
        EngineLabel = CandidateFormat.Engine(candidate.Engine);
        KindLabel = CandidateFormat.Kind(candidate.Kind);
        var score = CandidateFormat.Score(candidate.Score);
        var patentability = CandidateFormat.Patentability(candidate.Patentability);
        _score.Set(score);
        _patentability.Set(patentability);
        _evidence.Set(candidate.EvidenceCount.ToString(System.Globalization.CultureInfo.InvariantCulture));
        _linkMerge.Apply(Links, candidate.KnowledgeLinks);
        LinkedText = HistoryStrings.LinkedCount(Links.Count);
        AutomationName = HistoryStrings.CandidateAutomationName(
            Title,
            EngineLabel,
            KindLabel,
            new CandidateMetrics(score, patentability, candidate.EvidenceCount, Links.Count));
        OnPropertyChanged(nameof(HasLinks));
        OnPropertyChanged(nameof(HasNoLinks));
    }
}
