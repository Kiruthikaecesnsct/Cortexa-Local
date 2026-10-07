using Collector.Application.Knowledge;
using Collector.Domain.Enums;
using Collector.Presentation.Resources;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Collector.Presentation.ViewModels;

public sealed partial class KnowledgeItemRowViewModel : ObservableObject
{
    public KnowledgeItemRowViewModel(ExtractedKnowledgeItem item)
    {
        Item = item;
        KindLabel = ReviewStrings.KindLabel(item.Kind);
        SourceLine = SourceLineFormatter.Format(item.Source, item.DocumentName);
        EchoReasonText = ReviewStrings.EchoReasonText(item.EchoVerdict.Reason);
        IsIncluded = !item.EchoVerdict.IsEcho;
        ToggleCommand = new RelayCommand(Toggle, () => CanToggle);
    }

    public ExtractedKnowledgeItem Item { get; }

    public IRelayCommand ToggleCommand { get; }

    public KnowledgeKind Kind => Item.Kind;

    public string KindLabel { get; }

    public string Title => Item.Title;

    public string Summary => Item.Summary;

    public string? Details => Item.Details;

    public string? Excerpt => Item.Excerpt;

    public bool HasDetails => !string.IsNullOrWhiteSpace(Item.Details);

    public bool HasExcerpt => !string.IsNullOrWhiteSpace(Item.Excerpt);

    public string DocumentName => Item.DocumentName;

    public string DocumentPath => Item.DocumentPath;

    public string SourceLine { get; }

    public string HelpText => SourceLine;

    public bool IsFileSource => SourceLineFormatter.IsFileSource(Item.Source);

    public bool IsEcho => Item.EchoVerdict.IsEcho;

    public string? EchoReasonText { get; }

    public bool CanToggle => !IsLocked;

    public string AutomationName => ReviewStrings.ItemAutomationName(Title, KindLabel, IsIncluded, IsEcho, IsDocumentBlocked);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AutomationName))]
    public partial bool IsIncluded { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanToggle))]
    public partial bool IsLocked { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AutomationName))]
    public partial bool IsDocumentBlocked { get; set; }

    partial void OnIsLockedChanged(bool value) => ToggleCommand.NotifyCanExecuteChanged();

    private void Toggle() => IsIncluded = !IsIncluded;
}
