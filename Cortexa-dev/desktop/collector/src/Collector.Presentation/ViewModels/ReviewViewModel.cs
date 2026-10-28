using System.IO;
using Collector.Application.Auth;
using Collector.Application.Knowledge;
using Collector.Application.Ports;
using Collector.Presentation.Navigation;
using Collector.Presentation.Resources;
using Collector.Presentation.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Collector.Presentation.ViewModels;

public static class ReviewFocusKeys
{
    public const string Filters = "Filters";
    public const string GoToExtract = "GoToExtract";
    public const string Banner = "Banner";
    public const string ItemList = "ItemList";
    public const string CandidateList = "CandidateList";
}

public sealed partial class ReviewViewModel : FocusableViewModel, INavigationAware
{
    private static readonly IReadOnlyList<object> EmptyRows = [];

    private readonly KnowledgeRunState _state;
    private readonly INavigationService _navigation;
    private readonly IKnowledgePdfExporter _pdfExporter;
    private readonly IFilePicker _filePicker;
    private readonly ISessionState _authSession;
    private readonly TimeProvider _time;
    private ExtractionRunResult? _run;
    private IReadOnlyList<DocumentHeaderRowViewModel> _groups = [];
    private KindFilterViewModel? _activeFilter;
    private int _visibleItemCount;

    public ReviewViewModel(
        KnowledgeRunState state,
        INavigationService navigation,
        IKnowledgePdfExporter pdfExporter,
        IFilePicker filePicker,
        ISessionState authSession,
        TimeProvider time,
        CandidatesPanelViewModel candidates)
    {
        _state = state;
        _navigation = navigation;
        _pdfExporter = pdfExporter;
        _filePicker = filePicker;
        _authSession = authSession;
        _time = time;
        Candidates = candidates;
        Candidates.PropertyChanged += (_, _) => NotifyCandidateLayout();
        VisibleRows = [];
        Filters = [];
    }

    public bool ShowReady => _run is { Items.Count: > 0 };

    public CandidatesPanelViewModel Candidates { get; }

    public bool ShowEmptyCard => !ShowReady && !Candidates.IsVisible;

    public string EmptyTitle => _run is null ? ReviewStrings.NoRunTitle : ReviewStrings.EmptyTitle;

    public string EmptyBody => _run is null ? ReviewStrings.NoRunBody : ReviewStrings.EmptyBody;

    public string RunCaption => _run is null
        ? string.Empty
        : ReviewStrings.RunMeta(_run.Provider.ToString(), _run.Model, _run.PromptVersion, _run.Items.Count, _groups.Count);

    public KnowledgeItemRowViewModel? SelectedItem => SelectedRow as KnowledgeItemRowViewModel;

    public bool HasSelection => SelectedItem is not null;

    public bool ShowDetailsPlaceholder => SelectedItem is null;

    public bool ShowDownloadPdf => ShowReady && IncludedCount > 0;

    public int IncludedCount => _groups.Sum(group => group.IncludedCount);

    public int TotalCount => _run?.Items.Count ?? 0;

    public int HiddenCount => TotalCount - _visibleItemCount;

    public int BlockedDocumentCount => _groups.Count(group => group.IsBlocked);

    public bool HasBlocked => BlockedDocumentCount > 0;

    public string BlockedLine => ReviewStrings.BlockedLine(BlockedDocumentCount);

    public string CountLine => ReviewStrings.CountLine(IncludedCount, TotalCount, HiddenCount);

    public bool ShowNothingIncluded => ShowReady && IncludedCount == 0;

    public bool CanBulkEdit => ShowReady;

    [ObservableProperty]
    public partial IReadOnlyList<object> VisibleRows { get; set; }

    [ObservableProperty]
    public partial IReadOnlyList<KindFilterViewModel> Filters { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedItem), nameof(HasSelection), nameof(ShowDetailsPlaceholder))]
    public partial object? SelectedRow { get; set; }

    [ObservableProperty]
    public partial BannerViewModel? RunBanner { get; set; }

    [ObservableProperty]
    public partial BannerViewModel? ExportBanner { get; set; }

    public void OnNavigatedTo()
    {
        if (!ReferenceEquals(_run, _state.Current))
        {
            Load(_state.Current);
        }

        RequestFocus(InitialFocusKey());
    }

    public void OnNavigatedFrom()
    {
    }

    private string InitialFocusKey()
    {
        if (ShowReady)
        {
            return ReviewFocusKeys.Filters;
        }

        return Candidates.HasItems ? ReviewFocusKeys.CandidateList : ReviewFocusKeys.GoToExtract;
    }

    private void NotifyCandidateLayout()
    {
        OnPropertyChanged(nameof(ShowEmptyCard));
    }

    [RelayCommand]
    private void GoToExtract() => _navigation.NavigateTo(ScreenKeys.Extract);

    [RelayCommand(CanExecute = nameof(CanBulkEdit))]
    private void IncludeAll() => SetVisible(included: true, skipEchoes: true);

    [RelayCommand(CanExecute = nameof(CanBulkEdit))]
    private void ExcludeAll() => SetVisible(included: false, skipEchoes: false);

    [RelayCommand]
    private void DismissExportBanner() => ExportBanner = null;

    [RelayCommand(CanExecute = nameof(ShowDownloadPdf))]
    private async Task DownloadPdfAsync(CancellationToken cancellationToken)
    {
        var path = await _filePicker.PickSaveFileAsync(
            ReviewStrings.PdfSaveDialogTitle,
            ReviewStrings.PdfSaveDialogFilter,
            ReviewStrings.PdfDefaultFileName(_time.GetUtcNow()),
            cancellationToken);
        if (path is null)
        {
            return;
        }

        await ExportPdfAsync(path, cancellationToken);
    }

    private async Task ExportPdfAsync(string path, CancellationToken cancellationToken)
    {
        var report = new KnowledgePdfReport
        {
            GeneratedBy = _authSession.UserEmail ?? string.Empty,
            GeneratedAt = _time.GetUtcNow(),
            Provider = _run?.Provider.ToString() ?? string.Empty,
            Model = _run?.Model ?? string.Empty,
            Items = IncludedItems(),
        };

        try
        {
            await _pdfExporter.ExportAsync(report, path, cancellationToken);
            ExportBanner = new BannerViewModel(new BannerContent
            {
                Severity = BannerSeverity.Success,
                Title = ReviewStrings.PdfExportedTitle,
                Message = ReviewStrings.PdfExportedMessage(path),
                DismissCommand = DismissExportBannerCommand,
            });
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            ExportBanner = new BannerViewModel(new BannerContent
            {
                Severity = BannerSeverity.Error,
                Title = ReviewStrings.PdfExportFailedTitle,
                DismissCommand = DismissExportBannerCommand,
            });
        }
    }

    private void SetVisible(bool included, bool skipEchoes)
    {
        foreach (var group in _groups)
        {
            group.SetVisibleIncluded(included, skipEchoes);
        }
    }

    private void Load(ExtractionRunResult? run)
    {
        _run = run;
        ExportBanner = null;
        _groups = run is null ? [] : ReviewRowBuilder.BuildGroups(run.Items);
        foreach (var group in _groups)
        {
            group.SelectionChanged += (_, _) => RefreshFooter();
        }

        BuildFilters(run);
        RunBanner = run is null ? null : PartialBanner(run);
        SelectedRow = null;
        ApplyFilter(_activeFilter);
        OnPropertyChanged(nameof(ShowReady));
        NotifyCandidateLayout();
        OnPropertyChanged(nameof(EmptyTitle));
        OnPropertyChanged(nameof(EmptyBody));
        OnPropertyChanged(nameof(RunCaption));
    }

    private void BuildFilters(ExtractionRunResult? run)
    {
        Filters = run is null ? [] : KindFilterViewModel.Build(run.Items);
        _activeFilter = Filters.FirstOrDefault();
        foreach (var filter in Filters)
        {
            filter.PropertyChanged += OnFilterChanged;
        }
    }

    private void OnFilterChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (sender is KindFilterViewModel { IsSelected: true } filter && !ReferenceEquals(filter, _activeFilter))
        {
            ApplyFilter(filter);
        }
    }

    private void ApplyFilter(KindFilterViewModel? filter)
    {
        _activeFilter = filter;
        var previous = SelectedItem;
        var rows = filter is null ? EmptyRows : ReviewRowBuilder.Flatten(_groups, filter);
        _visibleItemCount = rows.Count(row => row is KnowledgeItemRowViewModel);
        VisibleRows = rows;
        SelectedRow = previous is not null && rows.Contains(previous)
            ? previous
            : rows.OfType<KnowledgeItemRowViewModel>().FirstOrDefault();
        RefreshFooter();
    }

    private void RefreshFooter()
    {
        OnPropertyChanged(nameof(IncludedCount));
        OnPropertyChanged(nameof(TotalCount));
        OnPropertyChanged(nameof(HiddenCount));
        OnPropertyChanged(nameof(CountLine));
        OnPropertyChanged(nameof(BlockedDocumentCount));
        OnPropertyChanged(nameof(HasBlocked));
        OnPropertyChanged(nameof(BlockedLine));
        OnPropertyChanged(nameof(ShowNothingIncluded));
        OnPropertyChanged(nameof(ShowDownloadPdf));
        DownloadPdfCommand.NotifyCanExecuteChanged();
    }

    private static BannerViewModel? PartialBanner(ExtractionRunResult run) =>
        run.FailedUnits == 0 && run.SkippedUnits == 0
            ? null
            : new BannerViewModel(new BannerContent
            {
                Severity = BannerSeverity.Warning,
                Title = ReviewStrings.RunPartialTitle,
                Message = ReviewStrings.RunPartialMessage(run.FailedUnits, run.SkippedUnits),
            });

    private List<ExtractedKnowledgeItem> IncludedItems() =>
    [
        .. _groups
            .Where(group => !group.IsBlocked)
            .SelectMany(group => group.Rows)
            .Where(row => row.IsIncluded)
            .Select(row => row.Item),
    ];
}
