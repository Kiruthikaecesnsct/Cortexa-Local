using System.ComponentModel;
using Collector.Application.Knowledge;
using Collector.Presentation.Resources;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Collector.Presentation.ViewModels;

public sealed partial class DocumentHeaderRowViewModel : ObservableObject
{
    private IReadOnlyList<KnowledgeItemRowViewModel> _visible;
    private bool _filterActive;
    private bool _bulkUpdating;

    public DocumentHeaderRowViewModel(string documentId, string filename, string sourcePath, IReadOnlyList<KnowledgeItemRowViewModel> rows)
    {
        DocumentId = documentId;
        Filename = filename;
        SourcePath = sourcePath;
        Rows = rows;
        _visible = rows;
        ToggleCommand = new RelayCommand(ToggleVisible, () => CanToggle);
        foreach (var row in rows)
        {
            row.PropertyChanged += OnRowChanged;
        }

        Refresh();
    }

    public event EventHandler? SelectionChanged;

    public string DocumentId { get; }

    public string Filename { get; }

    public string SourcePath { get; }

    public IReadOnlyList<KnowledgeItemRowViewModel> Rows { get; }

    public IRelayCommand ToggleCommand { get; }

    public string HelpText => SourcePath;

    public int TotalCount => Rows.Count;

    public int IncludedCount { get; private set; }

    public bool IsBlocked { get; private set; }

    public bool CanToggle => !IsLocked;

    public string CountText =>
        ReviewStrings.DocCount(IncludedCount, TotalCount) + (_filterActive ? ReviewStrings.DocShown(_visible.Count) : string.Empty);

    public string AutomationName => ReviewStrings.DocAutomationName(Filename, IncludedCount, TotalCount, IsBlocked);

    public bool? CheckState => VisibleIncludedState();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanToggle))]
    public partial bool IsLocked { get; set; }

    partial void OnIsLockedChanged(bool value)
    {
        foreach (var row in Rows)
        {
            row.IsLocked = value;
        }

        ToggleCommand.NotifyCanExecuteChanged();
    }

    public void ApplyFilter(IReadOnlyList<KnowledgeItemRowViewModel> visible, bool filterActive)
    {
        _visible = visible;
        _filterActive = filterActive;
        NotifyDisplay();
    }

    public void SetVisibleIncluded(bool included, bool skipEchoes)
    {
        _bulkUpdating = true;
        foreach (var row in _visible.Where(row => !(skipEchoes && row.IsEcho)))
        {
            row.IsIncluded = included;
        }

        _bulkUpdating = false;
        Refresh();
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    private void ToggleVisible() => SetVisibleIncluded(VisibleIncludedState() is not true, skipEchoes: false);

    private bool? VisibleIncludedState()
    {
        var included = _visible.Count(row => row.IsIncluded);
        if (included == 0)
        {
            return false;
        }

        return included == _visible.Count ? true : null;
    }

    private void OnRowChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_bulkUpdating || e.PropertyName != nameof(KnowledgeItemRowViewModel.IsIncluded))
        {
            return;
        }

        Refresh();
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    private void Refresh()
    {
        IncludedCount = Rows.Count(row => row.IsIncluded);
        IsBlocked = IncludedCount > UploadLimitsMirror.MaxItemsPerDocument;
        foreach (var row in Rows)
        {
            row.IsDocumentBlocked = IsBlocked;
        }

        NotifyDisplay();
    }

    private void NotifyDisplay()
    {
        OnPropertyChanged(nameof(IncludedCount));
        OnPropertyChanged(nameof(IsBlocked));
        OnPropertyChanged(nameof(CountText));
        OnPropertyChanged(nameof(AutomationName));
        OnPropertyChanged(nameof(CheckState));
    }
}
