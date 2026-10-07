using Collector.Application.Knowledge;
using Collector.Application.Upload;
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
}

public sealed partial class ReviewViewModel : FocusableViewModel, INavigationAware
{
    private static readonly IReadOnlyList<object> EmptyRows = [];

    private readonly KnowledgeRunState _state;
    private readonly ReviewUploader _uploader;
    private readonly INavigationService _navigation;
    private readonly IClipboard _clipboard;
    private ExtractionRunResult? _run;
    private IReadOnlyList<DocumentHeaderRowViewModel> _groups = [];
    private KindFilterViewModel? _activeFilter;
    private UploadSession? _session;
    private int _generation;
    private int _visibleItemCount;

    public ReviewViewModel(
        KnowledgeRunState state,
        ReviewUploader uploader,
        INavigationService navigation,
        IClipboard clipboard)
    {
        _state = state;
        _uploader = uploader;
        _navigation = navigation;
        _clipboard = clipboard;
        VisibleRows = [];
        Filters = [];
        Results = [];
    }

    public bool ShowReady => _run is { Items.Count: > 0 };

    public bool ShowEmptyCard => !ShowReady;

    public string EmptyTitle => _run is null ? ReviewStrings.NoRunTitle : ReviewStrings.EmptyTitle;

    public string EmptyBody => _run is null ? ReviewStrings.NoRunBody : ReviewStrings.EmptyBody;

    public string RunCaption => _run is null
        ? string.Empty
        : ReviewStrings.RunMeta(_run.Provider.ToString(), _run.Model, _run.PromptVersion, _run.Items.Count, _groups.Count);

    public KnowledgeItemRowViewModel? SelectedItem => SelectedRow as KnowledgeItemRowViewModel;

    public bool HasSelection => SelectedItem is not null;

    public bool ShowDetailsPlaceholder => SelectedItem is null;

    public bool ShowResults => Results.Count > 0;

    public bool ShowRetry => _session is not null && !_session.IsComplete && Results.Count > 0 && !IsUploading;

    public bool ShowUploadProgress => IsUploading;

    public int IncludedCount => _groups.Sum(group => group.IncludedCount);

    public int TotalCount => _run?.Items.Count ?? 0;

    public int HiddenCount => TotalCount - _visibleItemCount;

    public int UploadableCount => _groups.Where(group => !group.IsBlocked).Sum(group => group.IncludedCount);

    public int BlockedDocumentCount => _groups.Count(group => group.IsBlocked);

    public bool HasBlocked => BlockedDocumentCount > 0;

    public string BlockedLine => ReviewStrings.BlockedLine(BlockedDocumentCount);

    public string CountLine => ReviewStrings.CountLine(IncludedCount, TotalCount, HiddenCount);

    public bool ShowNothingIncluded => ShowReady && UploadableCount == 0 && !IsLocked;

    public string UploadLabel => IsUploadComplete ? ReviewStrings.UploadedButton : ReviewStrings.UploadButton(UploadableCount);

    public bool CanUpload => ShowReady && _session is null && !IsUploading && UploadableCount > 0;

    public bool CanBulkEdit => ShowReady && !IsLocked;

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
    public partial BannerViewModel? UploadBanner { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowResults), nameof(ShowRetry))]
    [NotifyCanExecuteChangedFor(nameof(RetryFailedCommand))]
    public partial IReadOnlyList<BatchResultRowViewModel> Results { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowNothingIncluded), nameof(CanBulkEdit))]
    [NotifyCanExecuteChangedFor(nameof(IncludeAllCommand), nameof(ExcludeAllCommand))]
    public partial bool IsLocked { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanUpload), nameof(ShowRetry), nameof(ShowUploadProgress))]
    [NotifyCanExecuteChangedFor(nameof(UploadCommand), nameof(RetryFailedCommand))]
    public partial bool IsUploading { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UploadLabel))]
    public partial bool IsUploadComplete { get; set; }

    [ObservableProperty]
    public partial string UploadProgressText { get; set; } = string.Empty;

    public void OnNavigatedTo()
    {
        if (!ReferenceEquals(_run, _state.Current))
        {
            Load(_state.Current);
        }

        RequestFocus(ShowReady ? ReviewFocusKeys.Filters : ReviewFocusKeys.GoToExtract);
    }

    public void OnNavigatedFrom()
    {
    }

    [RelayCommand]
    private void GoToExtract() => _navigation.NavigateTo(ScreenKeys.Extract);

    [RelayCommand(CanExecute = nameof(CanBulkEdit))]
    private void IncludeAll() => SetVisible(included: true, skipEchoes: true);

    [RelayCommand(CanExecute = nameof(CanBulkEdit))]
    private void ExcludeAll() => SetVisible(included: false, skipEchoes: false);

    [RelayCommand(CanExecute = nameof(CanUpload))]
    private Task UploadAsync(CancellationToken cancellationToken) => RunUploadAsync(cancellationToken);

    [RelayCommand(CanExecute = nameof(ShowRetry))]
    private Task RetryFailedAsync(CancellationToken cancellationToken) => RunUploadAsync(cancellationToken);

    private void SetVisible(bool included, bool skipEchoes)
    {
        foreach (var group in _groups)
        {
            group.SetVisibleIncluded(included, skipEchoes);
        }
    }

    private void Load(ExtractionRunResult? run)
    {
        _generation++;
        _run = run;
        _session = null;
        ResetUploadState();
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
        OnPropertyChanged(nameof(ShowEmptyCard));
        OnPropertyChanged(nameof(EmptyTitle));
        OnPropertyChanged(nameof(EmptyBody));
        OnPropertyChanged(nameof(RunCaption));
    }

    private void ResetUploadState()
    {
        IsLocked = false;
        IsUploading = false;
        IsUploadComplete = false;
        UploadBanner = null;
        UploadProgressText = string.Empty;
        Results = [];
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
        OnPropertyChanged(nameof(UploadableCount));
        OnPropertyChanged(nameof(BlockedDocumentCount));
        OnPropertyChanged(nameof(HasBlocked));
        OnPropertyChanged(nameof(BlockedLine));
        OnPropertyChanged(nameof(ShowNothingIncluded));
        OnPropertyChanged(nameof(UploadLabel));
        OnPropertyChanged(nameof(CanUpload));
        UploadCommand.NotifyCanExecuteChanged();
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

    private List<ExtractedKnowledgeItem> UploadItems() =>
    [
        .. _groups
            .Where(group => !group.IsBlocked)
            .SelectMany(group => group.Rows)
            .Where(row => row.IsIncluded)
            .Select(row => row.Item),
    ];

    private async Task RunUploadAsync(CancellationToken cancellationToken)
    {
        var generation = _generation;
        BeginUpload();
        var session = _session ?? await _uploader.PrepareAsync(_run!, UploadItems(), cancellationToken);
        if (session is not null)
        {
            await SendAsync(session, generation, cancellationToken);
        }

        if (generation != _generation)
        {
            return;
        }

        IsUploading = false;
        Present(session);
        RequestFocus(ReviewFocusKeys.Banner);
    }

    private void BeginUpload()
    {
        foreach (var group in _groups)
        {
            group.IsLocked = true;
        }

        IsLocked = true;
        UploadBanner = null;
        IsUploading = true;
    }

    private async Task SendAsync(UploadSession session, int generation, CancellationToken cancellationToken)
    {
        if (generation != _generation)
        {
            return;
        }

        _session = session;
        ShowProgress(session);
        var progress = new SyncProgress<BatchUploadResult>(_ => ShowProgress(session, generation));
        await _uploader.SendAsync(session, progress, cancellationToken);
    }

    private void ShowProgress(UploadSession session, int? generation = null)
    {
        if (generation is { } value && value != _generation)
        {
            return;
        }

        var results = session.Results;
        var done = results.Count(result => result.Status != UploadBatchStatus.Pending);
        UploadProgressText = ReviewStrings.UploadingProgress(Math.Min(done + 1, results.Count), results.Count);
        Results = [.. results.Where(result => result.Status != UploadBatchStatus.Pending).Select(result => ToRow(result, results.Count))];
    }

    private BatchResultRowViewModel ToRow(BatchUploadResult result, int total) => new(result, total, _clipboard);

    private void Present(UploadSession? session)
    {
        UploadBanner = UploadOutcomeBanner.For(session);
        IsUploadComplete = session?.IsComplete == true;
        OnPropertyChanged(nameof(ShowRetry));
        OnPropertyChanged(nameof(CanUpload));
        RetryFailedCommand.NotifyCanExecuteChanged();
    }
}
