using System.Collections.ObjectModel;
using System.ComponentModel;
using Collector.Application.Extraction;
using Collector.Application.Knowledge;
using Collector.Application.Ports;
using Collector.Domain.Enums;
using Collector.Domain.Extraction;
using Collector.Infrastructure.Options;
using Collector.Presentation.Resources;
using Collector.Presentation.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Collector.Presentation.ViewModels;

public sealed record ExtractionDependencies(
    ExtractionService ExtractionService,
    IUnitStore UnitStore,
    IFilePicker FilePicker,
    RemoteSourceViewModel Remote,
    ILogger<ExtractionViewModel> Logger);

public sealed record ExtractionBatch(IReadOnlyList<string> Paths, SourceType Source, string? Origin = null);

public static class ExtractionFocusKeys
{
    public const string Documents = "Documents";
}

public sealed record TokenEstimationDependencies(TokenEstimator Estimator, ProviderOutputLimits OutputLimits);

public sealed partial class ExtractionViewModel : FocusableViewModel
{
    private readonly ExtractionService _extractionService;
    private readonly IUnitStore _unitStore;
    private readonly IFilePicker _filePicker;
    private readonly ILogger<ExtractionViewModel> _logger;
    private readonly TokenEstimator _tokenEstimator;
    private readonly ProviderOutputLimits _outputLimits;
    private readonly ProviderModelCatalog _modelCatalog;
    private int _estimateGeneration;

    public ExtractionViewModel(
        ExtractionDependencies dependencies,
        KnowledgeRunViewModel knowledge,
        TokenEstimationDependencies tokenEstimation,
        IOptions<ProviderModelCatalog> modelCatalog)
    {
        _extractionService = dependencies.ExtractionService;
        _unitStore = dependencies.UnitStore;
        _filePicker = dependencies.FilePicker;
        _logger = dependencies.Logger;
        Remote = dependencies.Remote;
        _tokenEstimator = tokenEstimation.Estimator;
        _outputLimits = tokenEstimation.OutputLimits;
        _modelCatalog = modelCatalog.Value;
        Knowledge = knowledge;
        Documents = [];
        PreviewUnits = [];
        AvailableModels = [];
        knowledge.FocusRequested += (_, key) => RequestFocus(key);
        knowledge.PropertyChanged += OnKnowledgeChanged;
        Remote.FocusRequested += (_, key) => RequestFocus(key);
        Remote.PropertyChanged += OnRemoteChanged;
        Remote.FilesFetched += OnFilesFetched;
        Knowledge.Provider = SelectedProvider;
        UpdateAvailableModels(SelectedProvider);
    }

    public KnowledgeRunViewModel Knowledge { get; }

    public RemoteSourceViewModel Remote { get; }

    public ObservableCollection<DocumentRowViewModel> Documents { get; }

    public ObservableCollection<UnitPreviewItemViewModel> PreviewUnits { get; }

    public IReadOnlyList<CollectorProvider> AvailableProviders { get; } = Enum.GetValues<CollectorProvider>();

    public ObservableCollection<string> AvailableModels { get; }

    public IEnumerable<DocumentRowViewModel> SkipRows => Documents.Where(row => row.IsSkipped);

    public bool HasDocuments => Documents.Count > 0;

    public bool IsEmptyState => !HasDocuments;

    public bool ShowLocalEmpty => IsEmptyState && Remote.IsLocal;

    public int FilesCount => Documents.Count;

    public int ExtractedCount => Documents.Count(row => row.Status == DocumentStatus.Extracted);

    public int SkippedCount => Documents.Count(row => row.IsSkipped);

    public int TotalUnits => Documents.Sum(row => row.UnitCount);

    public int TotalTokens => Documents.Sum(row => row.TokenCount);

    public bool HasSkipped => SkippedCount > 0;

    public bool AllSkipped => FilesCount > 0 && SkippedCount == FilesCount;

    public string StatusCaption => IsExtracting ? ProgressText : ExtractionStrings.ReadyStatus(FilesCount);

    public bool CanEditDocuments => !IsExtracting && !Knowledge.IsRunning && !Remote.IsFetching;

    public bool ShowPreviewPlaceholder => SelectedDocument is null;

    public bool ShowPreviewEmpty => SelectedDocument is not null && SelectedDocument.Status != DocumentStatus.Extracted;

    public bool ShowPreviewUnits => SelectedDocument is not null && SelectedDocument.Status == DocumentStatus.Extracted;

    public string PreviewHeader => SelectedDocument is null
        ? string.Empty
        : ExtractionStrings.UnitPreviewHeader(SelectedDocument.Filename, SelectedDocument.UnitCount, SelectedDocument.TokenCount);

    [ObservableProperty]
    [NotifyPropertyChangedFor(
        nameof(ShowPreviewPlaceholder),
        nameof(ShowPreviewEmpty),
        nameof(ShowPreviewUnits),
        nameof(PreviewHeader))]
    public partial DocumentRowViewModel? SelectedDocument { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusCaption), nameof(CanEditDocuments))]
    [NotifyCanExecuteChangedFor(nameof(PickFilesCommand), nameof(ClearAllCommand), nameof(RemoveDocumentCommand))]
    public partial bool IsExtracting { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusCaption))]
    public partial string ProgressText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial BannerViewModel? SkipBanner { get; set; }

    [ObservableProperty]
    public partial CollectorProvider SelectedProvider { get; set; } = CollectorProvider.Claude;

    [ObservableProperty]
    public partial string? SelectedModel { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TotalEstimatedTokens))]
    public partial int PromptTokens { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TotalEstimatedTokens))]
    public partial int EstimatedOutputTokens { get; set; }

    public int TotalEstimatedTokens => PromptTokens + EstimatedOutputTokens;

    partial void OnIsExtractingChanged(bool value)
    {
        Knowledge.IsParsing = value;
        SyncRemoteLock();
    }

    partial void OnSelectedDocumentChanged(DocumentRowViewModel? value) => _ = LoadPreviewAsync(value, CancellationToken.None);

    partial void OnSelectedProviderChanged(CollectorProvider value)
    {
        Knowledge.Provider = value;
        UpdateAvailableModels(value);
        _ = RecomputeTokenEstimateAsync();
    }

    partial void OnSelectedModelChanged(string? value)
    {
        Knowledge.Model = value;
        _ = RecomputeTokenEstimateAsync();
    }

    private void UpdateAvailableModels(CollectorProvider provider)
    {
        AvailableModels.Clear();
        if (!_modelCatalog.Providers.TryGetValue(provider, out var entry))
        {
            return;
        }

        foreach (var model in entry.Models)
        {
            AvailableModels.Add(model);
        }

        SelectedModel = entry.DefaultModel;
    }

    [RelayCommand(CanExecute = nameof(CanPickFiles))]
    private async Task PickFilesAsync(CancellationToken cancellationToken)
    {
        var paths = await _filePicker.PickFilesAsync(cancellationToken);
        if (paths.Count == 0)
        {
            return;
        }

        await RunExtractionAsync(new ExtractionBatch(paths, SourceType.Local), cancellationToken);
    }

    private async Task RunExtractionAsync(ExtractionBatch batch, CancellationToken cancellationToken)
    {
        IsExtracting = true;
        try
        {
            await ExtractAllAsync(batch, cancellationToken);
        }
        finally
        {
            IsExtracting = false;
            RefreshDerived();
        }
    }

    private void OnFilesFetched(object? sender, RemoteFilesFetchedEventArgs e) => _ = ExtractFetchedAsync(e);

    private async Task ExtractFetchedAsync(RemoteFilesFetchedEventArgs fetched)
    {
        var batch = new ExtractionBatch(fetched.Paths, fetched.Source, fetched.Origin);
        try
        {
            await RunExtractionAsync(batch, CancellationToken.None);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Extraction of fetched repository files failed.");
        }

        RequestFocus(ExtractionFocusKeys.Documents);
    }

    private bool CanPickFiles() => CanEditDocuments;

    [RelayCommand(CanExecute = nameof(CanEditDocuments))]
    private void ClearAll()
    {
        foreach (var row in Documents)
        {
            row.PropertyChanged -= OnRowChanged;
        }

        Documents.Clear();
        PreviewUnits.Clear();
        SelectedDocument = null;
        SkipBanner = null;
        RefreshDerived();
    }

    [RelayCommand(CanExecute = nameof(CanEditDocuments))]
    private void RemoveDocument(DocumentRowViewModel? row)
    {
        if (row is null)
        {
            return;
        }

        row.PropertyChanged -= OnRowChanged;
        Documents.Remove(row);
        if (SelectedDocument == row)
        {
            SelectedDocument = Documents.FirstOrDefault(d => d.Status == DocumentStatus.Extracted);
        }

        RefreshDerived();
    }

    private async Task ExtractAllAsync(ExtractionBatch batch, CancellationToken cancellationToken)
    {
        for (var i = 0; i < batch.Paths.Count; i++)
        {
            ProgressText = ExtractionStrings.ParsingProgress(i + 1, batch.Paths.Count);
            await ExtractOneAsync(batch.Paths[i], batch, cancellationToken);
        }
    }

    private async Task ExtractOneAsync(string path, ExtractionBatch batch, CancellationToken cancellationToken)
    {
        var row = new DocumentRowViewModel(path, batch.Origin);
        row.PropertyChanged += OnRowChanged;
        Documents.Add(row);
        RefreshDerived();

        row.Status = DocumentStatus.Extracting;
        var sourceKind = FileClassifier.Classify(path) == FileClassification.Code ? SourceKind.Code : SourceKind.Paper;
        try
        {
            var results = await _extractionService.ExtractAsync([path], batch.Source, sourceKind, cancellationToken);
            await ApplyResultAsync(row, results[0], cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Unexpected failure extracting {FilePath}.", path);
            row.ApplyResult(new ExtractionResult { SourcePath = path, Status = DocumentStatus.Failed, Reason = ex.Message });
        }

        EnsureDefaultSelection();
        UpdateSkipBanner();
    }

    private async Task ApplyResultAsync(DocumentRowViewModel row, ExtractionResult result, CancellationToken cancellationToken)
    {
        row.ApplyResult(result);
        if (result.Status != DocumentStatus.Extracted || result.DocumentId is null)
        {
            return;
        }

        var units = await _unitStore.GetByDocumentIdAsync(result.DocumentId, cancellationToken);
        row.ApplyUnitTotals(units.Count, units.Sum(u => u.TokenCount));
    }

    private void EnsureDefaultSelection()
    {
        if (SelectedDocument is not null)
        {
            return;
        }

        SelectedDocument = Documents.FirstOrDefault(row => row.Status == DocumentStatus.Extracted);
    }

    private void UpdateSkipBanner()
    {
        if (!HasSkipped)
        {
            SkipBanner = null;
            return;
        }

        SkipBanner = AllSkipped
            ? new BannerViewModel(new BannerContent
            {
                Severity = BannerSeverity.Error,
                Title = ExtractionStrings.AllSkippedTitle,
                Message = ExtractionStrings.AllSkippedMessage,
            })
            : new BannerViewModel(new BannerContent
            {
                Severity = BannerSeverity.Warning,
                Title = ExtractionStrings.PartialSkipTitle,
                Message = ExtractionStrings.PartialSkipMessage,
            });
    }

    private async Task LoadPreviewAsync(DocumentRowViewModel? row, CancellationToken cancellationToken)
    {
        PreviewUnits.Clear();
        if (row?.DocumentId is null || row.Status != DocumentStatus.Extracted)
        {
            return;
        }

        try
        {
            var units = await _unitStore.GetByDocumentIdAsync(row.DocumentId, cancellationToken);
            ShowPreviewIfStillSelected(row, units);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Could not load extraction units for {DocumentId}.", row.DocumentId);
        }
    }

    // A newer selection may have started its own load while this one awaited the store.
    private void ShowPreviewIfStillSelected(DocumentRowViewModel row, IReadOnlyList<Domain.Extraction.ExtractionUnit> units)
    {
        if (!ReferenceEquals(row, SelectedDocument))
        {
            return;
        }

        foreach (var unit in units.OrderBy(u => u.Ordinal))
        {
            PreviewUnits.Add(new UnitPreviewItemViewModel(unit, row.SourcePath));
        }
    }

    private void OnRowChanged(object? sender, EventArgs e)
    {
        RefreshDerived();
        if (sender == SelectedDocument)
        {
            OnPropertyChanged(nameof(PreviewHeader));
        }
    }

    private void RefreshDerived()
    {
        OnPropertyChanged(nameof(HasDocuments));
        OnPropertyChanged(nameof(IsEmptyState));
        OnPropertyChanged(nameof(ShowLocalEmpty));
        OnPropertyChanged(nameof(FilesCount));
        OnPropertyChanged(nameof(ExtractedCount));
        OnPropertyChanged(nameof(SkippedCount));
        OnPropertyChanged(nameof(TotalUnits));
        OnPropertyChanged(nameof(TotalTokens));
        OnPropertyChanged(nameof(HasSkipped));
        OnPropertyChanged(nameof(AllSkipped));
        OnPropertyChanged(nameof(StatusCaption));
        OnPropertyChanged(nameof(SkipRows));
        SyncKnowledgeDocuments();
        _ = RecomputeTokenEstimateAsync();
    }

    private void SyncKnowledgeDocuments() => Knowledge.SetDocuments(ExtractedDocumentIds());

    private List<string> ExtractedDocumentIds() =>
        [.. Documents.Where(row => row.Status == DocumentStatus.Extracted && row.DocumentId is not null).Select(row => row.DocumentId!)];

    private async Task RecomputeTokenEstimateAsync()
    {
        var generation = Interlocked.Increment(ref _estimateGeneration);
        List<ExtractionUnit> units;
        try
        {
            units = await LoadUnitsAsync(ExtractedDocumentIds(), CancellationToken.None);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Could not compute the token estimate.");
            return;
        }

        if (generation != _estimateGeneration)
        {
            return;
        }

        ApplyEstimate(units);
    }

    private async Task<List<ExtractionUnit>> LoadUnitsAsync(IReadOnlyList<string> documentIds, CancellationToken cancellationToken)
    {
        var units = new List<ExtractionUnit>();
        foreach (var documentId in documentIds)
        {
            units.AddRange(await _unitStore.GetByDocumentIdAsync(documentId, cancellationToken));
        }

        return units;
    }

    private void ApplyEstimate(List<ExtractionUnit> units)
    {
        var maxOutputTokens = _outputLimits.MaxOutputTokensFor(SelectedProvider);
        var estimate = _tokenEstimator.Estimate(units, maxOutputTokens);
        PromptTokens = estimate.PromptTokens;
        EstimatedOutputTokens = estimate.EstimatedOutputTokens;
    }

    private void OnKnowledgeChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(KnowledgeRunViewModel.IsRunning))
        {
            return;
        }

        RefreshEditability();
        SyncRemoteLock();
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
