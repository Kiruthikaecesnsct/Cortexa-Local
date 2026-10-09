using System.ComponentModel;
using Collector.Application.Extraction;
using Collector.Application.Knowledge;
using Collector.Application.Ports;
using Collector.Domain.Enums;
using Collector.Presentation.Behaviors;
using Collector.Presentation.Navigation;
using Collector.Presentation.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace Collector.Presentation.ViewModels;

public sealed record ExtractionDependencies(
    ExtractionService ExtractionService,
    IUnitStore UnitStore,
    IFilePicker FilePicker,
    RemoteSourceViewModel Remote,
    ILogger<ExtractionViewModel> Logger,
    IntakeProgressViewModel Intake,
    ParallelSplitter Splitter,
    TimeProvider Time);

public sealed record ExtractionBatch(IReadOnlyList<SplitFile> Files, SourceType Source, string? Origin = null);

public static class ExtractionFocusKeys
{
    public const string Documents = "Documents";
}

public sealed record TokenEstimationDependencies(TokenEstimator Estimator, ProviderOutputLimits OutputLimits);

public sealed partial class ExtractionViewModel : FocusableViewModel, INavigationAware
{
    private readonly IUnitStore _unitStore;
    private readonly IFilePicker _filePicker;
    private readonly ILogger<ExtractionViewModel> _logger;
    private readonly ParallelSplitter _splitter;
    private readonly TimeProvider _time;
    private readonly TokenEstimator _tokenEstimator;
    private readonly ProviderOutputLimits _outputLimits;
    private readonly DocumentTally _tally = new();

    public ExtractionViewModel(
        ExtractionDependencies dependencies,
        KnowledgeRunViewModel knowledge,
        TokenEstimationDependencies tokenEstimation,
        ActiveModelViewModel activeModel)
    {
        _unitStore = dependencies.UnitStore;
        _filePicker = dependencies.FilePicker;
        _logger = dependencies.Logger;
        _splitter = dependencies.Splitter;
        _time = dependencies.Time;
        Intake = dependencies.Intake;
        Remote = dependencies.Remote;
        _tokenEstimator = tokenEstimation.Estimator;
        _outputLimits = tokenEstimation.OutputLimits;
        ActiveModel = activeModel;
        Knowledge = knowledge;
        Documents = [];
        PreviewUnits = [];
        DocumentsView = CreateDocumentsView();
        SkipRowsView = CreateSkipRowsView();
        _searchTimer = CreateSearchTimer();
        WireEvents();
        SourceCards = CreateSourceCards();
        SyncSourceCards();
        PushActiveModel();
    }

    public KnowledgeRunViewModel Knowledge { get; }

    public ActiveModelViewModel ActiveModel { get; }

    public RemoteSourceViewModel Remote { get; }

    public IntakeProgressViewModel Intake { get; }

    public IReadOnlyList<SourceCardViewModel> SourceCards { get; }

    public RangeObservableCollection<DocumentRowViewModel> Documents { get; }

    public bool CanEditDocuments => !IsExtracting && !Knowledge.IsRunning && !Remote.IsFetching;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusCaption), nameof(CanEditDocuments))]
    [NotifyCanExecuteChangedFor(nameof(PickFilesCommand), nameof(ClearAllCommand), nameof(RemoveDocumentCommand))]
    public partial bool IsExtracting { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusCaption))]
    public partial string ProgressText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial BannerViewModel? SkipBanner { get; set; }

    partial void OnIsExtractingChanged(bool value)
    {
        Knowledge.IsParsing = value;
        SyncRemoteLock();
    }

    public void OnNavigatedTo() => _ = ActiveModel.RefreshAsync(CancellationToken.None);

    public void OnNavigatedFrom()
    {
    }

    private void WireEvents()
    {
        Knowledge.FocusRequested += (_, key) => RequestFocus(key);
        Knowledge.PropertyChanged += OnKnowledgeChanged;
        Remote.FocusRequested += (_, key) => RequestFocus(key);
        Remote.PropertyChanged += OnRemoteChanged;
        Remote.FilesFetched += OnFilesFetched;
        Intake.PropertyChanged += OnIntakeChanged;
        ActiveModel.Updated += (_, _) => OnActiveModelUpdated();
    }

    private void OnActiveModelUpdated()
    {
        PushActiveModel();
        RecomputeEstimate();
    }

    private void PushActiveModel()
    {
        Knowledge.Provider = ActiveModel.Provider;
        Knowledge.Model = ActiveModel.Model;
        Knowledge.Readiness = ActiveModel.Readiness;
    }

    [RelayCommand(CanExecute = nameof(CanEditDocuments))]
    private async Task PickFilesAsync(CancellationToken cancellationToken)
    {
        var paths = await _filePicker.PickFilesAsync(cancellationToken);
        if (paths.Count == 0)
        {
            return;
        }

        var files = paths.Select(path => new SplitFile(path)).ToList();
        await RunExtractionAsync(new ExtractionBatch(files, SourceType.Local));
    }

    [RelayCommand(CanExecute = nameof(CanEditDocuments))]
    private void ClearAll()
    {
        Documents.Clear();
        _tally.Reset();
        SelectedDocument = null;
        SkipBanner = null;
        _skipBannerKind = SkipBannerKind.None;
        RefreshDerived();
    }

    [RelayCommand(CanExecute = nameof(CanEditDocuments))]
    private void RemoveDocument(DocumentRowViewModel? row)
    {
        if (row is null || !Documents.Remove(row))
        {
            return;
        }

        _tally.Remove(row);
        if (SelectedDocument == row)
        {
            SelectedDocument = Documents.FirstOrDefault(d => d.Status == DocumentStatus.Extracted);
        }

        RefreshDerived();
    }

    [RelayCommand(CanExecute = nameof(HasCancelableWork))]
    private void CancelActiveWork()
    {
        if (Intake.IsActive)
        {
            Intake.CancelCommand.Execute(null);
            return;
        }

        Knowledge.ExtractKnowledgeCancelCommand.Execute(null);
    }

    private bool HasCancelableWork() => Intake.IsActive || Knowledge.IsRunning;

    private void OnIntakeChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(IntakeProgressViewModel.IsActive))
        {
            CancelActiveWorkCommand.NotifyCanExecuteChanged();
        }
    }

    private void OnKnowledgeChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(KnowledgeRunViewModel.IsRunning))
        {
            return;
        }

        RefreshEditability();
        SyncRemoteLock();
        CancelActiveWorkCommand.NotifyCanExecuteChanged();
    }

    private void OnRemoteChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(RemoteSourceViewModel.IsFetching))
        {
            RefreshEditability();
        }

        if (e.PropertyName == nameof(RemoteSourceViewModel.IsLocal))
        {
            OnPropertyChanged(nameof(ShowLocalEmpty));
        }

        if (e.PropertyName == nameof(RemoteSourceViewModel.SelectedSource))
        {
            SyncSourceCards();
        }
    }

    private SourceCardViewModel[] CreateSourceCards() =>
        [.. SourceCardCatalog.All.Select(content => new SourceCardViewModel(content, SelectSource))];

    private void SelectSource(SourceType source)
    {
        if (Remote.AreControlsEnabled)
        {
            Remote.SelectedSource = source;
            return;
        }

        SyncSourceCards();
    }

    private void SyncSourceCards()
    {
        foreach (var card in SourceCards)
        {
            card.Sync(Remote.SelectedSource);
        }
    }

    private void SyncRemoteLock() => Remote.SetLocked(IsExtracting || Knowledge.IsRunning);

    private void RefreshEditability()
    {
        OnPropertyChanged(nameof(CanEditDocuments));
        PickFilesCommand.NotifyCanExecuteChanged();
        ClearAllCommand.NotifyCanExecuteChanged();
        RemoveDocumentCommand.NotifyCanExecuteChanged();
    }
}
