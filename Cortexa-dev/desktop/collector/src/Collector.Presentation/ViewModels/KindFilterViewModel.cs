using Collector.Application.Knowledge;
using Collector.Domain.Enums;
using Collector.Presentation.Resources;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Collector.Presentation.ViewModels;

public sealed partial class KindFilterViewModel : ObservableObject
{
    private static readonly KnowledgeKind[] DisplayOrder =
    [
        KnowledgeKind.Logic,
        KnowledgeKind.Algorithm,
        KnowledgeKind.Method,
        KnowledgeKind.Layer,
        KnowledgeKind.DataModel,
        KnowledgeKind.Interface,
        KnowledgeKind.Workflow,
        KnowledgeKind.KeyContent,
    ];

    private KindFilterViewModel(KnowledgeKind? kind, string label, int count)
    {
        Kind = kind;
        Count = count;
        Label = ReviewStrings.ChipLabel(label, count);
        AutomationName = ReviewStrings.ChipName(label, count);
    }

    public KnowledgeKind? Kind { get; }

    public int Count { get; }

    public string Label { get; }

    public string AutomationName { get; }

    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    public static IReadOnlyList<KindFilterViewModel> Build(IReadOnlyList<ExtractedKnowledgeItem> items)
    {
        var filters = new List<KindFilterViewModel> { new(null, ReviewStrings.FilterAll, items.Count) { IsSelected = true } };
        foreach (var kind in DisplayOrder)
        {
            var count = items.Count(item => item.Kind == kind);
            if (count > 0)
            {
                filters.Add(new KindFilterViewModel(kind, ReviewStrings.KindLabel(kind), count));
            }
        }

        return filters;
    }

    public bool Matches(KnowledgeItemRowViewModel row) => Kind is null || row.Kind == Kind;
}
