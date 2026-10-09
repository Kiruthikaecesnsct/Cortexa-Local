using System.Collections;
using System.ComponentModel;
using System.Windows.Data;
using Collector.Domain.Enums;
using Collector.Presentation.Resources;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Collector.Presentation.ViewModels;

public sealed partial class ExtractionViewModel
{
    private static readonly TimeSpan SearchDebounce = TimeSpan.FromMilliseconds(250);

    private static readonly string[] DerivedNames =
    [
        nameof(HasDocuments),
        nameof(IsEmptyState),
        nameof(ShowLocalEmpty),
        nameof(FilesCount),
        nameof(AnalyzedCount),
        nameof(ExtractedCount),
        nameof(SkippedCount),
        nameof(TotalUnits),
        nameof(TotalTokens),
        nameof(HasSkipped),
        nameof(AllSkipped),
        nameof(StatusCaption),
        nameof(DocumentsShowingText),
        nameof(ShowNoDocumentMatch),
    ];

    private readonly ITimer _searchTimer;
    private bool _holdSelection;
    private bool _clearingFilters;
    private SkipBannerKind _skipBannerKind;

    private enum SkipBannerKind
    {
        None,
        Partial,
        All,
    }

    public ListCollectionView DocumentsView { get; }

    public ListCollectionView SkipRowsView { get; }

    public IEnumerable SkipRows => SkipRowsView;

    public DocumentSortColumn? SortColumn { get; private set; }

    public ListSortDirection SortDirection { get; private set; } = ListSortDirection.Ascending;

    public bool HasDocuments => _tally.Files > 0;

    public bool IsEmptyState => !HasDocuments;

    public bool ShowLocalEmpty => IsEmptyState && Remote.IsLocal;

    public int FilesCount => _tally.Files;

    public int AnalyzedCount => _tally.Analyzed;

    public int ExtractedCount => _tally.Analyzed;

    public int SkippedCount => _tally.Skipped;

    public int TotalUnits => _tally.Units;

    public int TotalTokens => _tally.Tokens;

    public bool HasSkipped => SkippedCount > 0;

    public bool AllSkipped => FilesCount > 0 && SkippedCount == FilesCount;

    public string StatusCaption => IsExtracting ? ProgressText : ExtractionStrings.ReadyStatus(FilesCount);

    public string DocumentsShowingText => ExtractionStrings.DocumentsShowing(DocumentsView.Count, FilesCount);

    public bool ShowNoDocumentMatch => HasDocuments && DocumentsView.Count == 0;

    [ObservableProperty]
    public partial DocumentStatusFilter StatusFilter { get; set; }

    [ObservableProperty]
    public partial string DocumentSearch { get; set; } = string.Empty;

    partial void OnStatusFilterChanged(DocumentStatusFilter value)
    {
        if (!_clearingFilters)
        {
            ApplyFilterChange();
        }
    }

    partial void OnDocumentSearchChanged(string value)
    {
        if (!_clearingFilters)
        {
            _searchTimer.Change(SearchDebounce, Timeout.InfiniteTimeSpan);
        }
    }

    public ListSortDirection SortBy(string sortMemberPath)
    {
        if (!Enum.TryParse<DocumentSortColumn>(sortMemberPath, out var column))
        {
            return SortDirection;
        }

        SortDirection = SortColumn == column && SortDirection == ListSortDirection.Ascending
            ? ListSortDirection.Descending
            : ListSortDirection.Ascending;
        SortColumn = column;
        PreserveSelection(() => DocumentsView.CustomSort = new DocumentSortComparer(column, SortDirection));
        return SortDirection;
    }

    [RelayCommand]
    private void ClearDocumentFilters()
    {
        _clearingFilters = true;
        try
        {
            StatusFilter = DocumentStatusFilter.All;
            DocumentSearch = string.Empty;
        }
        finally
        {
            _clearingFilters = false;
        }

        ApplyFilterChange();
    }

    private ITimer CreateSearchTimer() =>
        _time.CreateTimer(_ => Services.UiThread.Post(ApplyFilterChange), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);

    private ListCollectionView CreateDocumentsView() =>
        new(Documents) { Filter = item => item is DocumentRowViewModel row && MatchesFilter(row) };

    private ListCollectionView CreateSkipRowsView() =>
        new(Documents) { Filter = item => item is DocumentRowViewModel { IsSkipped: true } };

    private bool MatchesFilter(DocumentRowViewModel row) => DocumentFilter.Matches(row, StatusFilter, DocumentSearch);

    private void ApplyFilterChange()
    {
        _searchTimer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        if (SelectedDocument is { } selected && !MatchesFilter(selected))
        {
            SelectedDocument = null;
        }

        PreserveSelection(DocumentsView.Refresh);
        OnPropertyChanged(nameof(DocumentsShowingText));
        OnPropertyChanged(nameof(ShowNoDocumentMatch));
    }

    private void PreserveSelection(Action change)
    {
        var selected = SelectedDocument;
        _holdSelection = true;
        try
        {
            change();
            if (!ReferenceEquals(SelectedDocument, selected))
            {
                SelectedDocument = selected;
            }
        }
        finally
        {
            _holdSelection = false;
        }
    }

    private void RefreshDerived()
    {
        foreach (var name in DerivedNames)
        {
            OnPropertyChanged(name);
        }

        Knowledge.SetDocuments([.. _tally.AnalyzedIds]);
        RecomputeEstimate();
        UpdateSkipBanner();
    }

    private void UpdateSkipBanner()
    {
        var kind = !HasSkipped ? SkipBannerKind.None : AllSkipped ? SkipBannerKind.All : SkipBannerKind.Partial;
        if (kind == _skipBannerKind)
        {
            return;
        }

        _skipBannerKind = kind;
        SkipBanner = kind switch
        {
            SkipBannerKind.All => CreateBanner(BannerSeverity.Error, ExtractionStrings.AllSkippedTitle, ExtractionStrings.AllSkippedMessage),
            SkipBannerKind.Partial => CreateBanner(BannerSeverity.Warning, ExtractionStrings.PartialSkipTitle, ExtractionStrings.PartialSkipMessage),
            _ => null,
        };
    }

    private static BannerViewModel CreateBanner(BannerSeverity severity, string title, string message) =>
        new(new BannerContent { Severity = severity, Title = title, Message = message });
}
