using System.Collections.ObjectModel;
using Collector.Application.Extraction;
using Collector.Application.Ports;
using Collector.Domain.Enums;
using Collector.Presentation.Resources;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace Collector.Presentation.ViewModels;

public sealed partial class ExtractionViewModel : FocusableViewModel
{
    private readonly ExtractionService _extractionService;
    private readonly IUnitStore _unitStore;
    private readonly IFilePicker _filePicker;
    private readonly ILogger<ExtractionViewModel> _logger;

    public ExtractionViewModel(
        ExtractionService extractionService,
        IUnitStore unitStore,
        IFilePicker filePicker,
        ILogger<ExtractionViewModel> logger)
    {
        _extractionService = extractionService;
        _unitStore = unitStore;
        _filePicker = filePicker;
        _logger = logger;
        Documents = [];
        PreviewUnits = [];
    }

    public ObservableCollection<DocumentRowViewModel> Documents { get; }

    public ObservableCollection<UnitPreviewItemViewModel> PreviewUnits { get; }

    public IEnumerable<DocumentRowViewModel> SkipRows => Documents.Where(row => row.IsSkipped);

    public bool HasDocuments => Documents.Count > 0;

    public bool IsEmptyState => !HasDocuments;

    public int FilesCount => Documents.Count;

    public int ExtractedCount => Documents.Count(row => row.Status == DocumentStatus.Extracted);

    public int SkippedCount => Documents.Count(row => row.IsSkipped);

    public int TotalUnits => Documents.Sum(row => row.UnitCount);

    public int TotalTokens => Documents.Sum(row => row.TokenCount);

    public bool HasSkipped => SkippedCount > 0;

    public bool AllSkipped => FilesCount > 0 && SkippedCount == FilesCount;

    public string StatusCaption => IsExtracting ? ProgressText : ExtractionStrings.ReadyStatus(FilesCount);

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
    [NotifyPropertyChangedFor(nameof(StatusCaption))]
    [NotifyCanExecuteChangedFor(nameof(PickFilesCommand))]
    public partial bool IsExtracting { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusCaption))]
    public partial string ProgressText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial BannerViewModel? SkipBanner { get; set; }

    partial void OnSelectedDocumentChanged(DocumentRowViewModel? value) => _ = LoadPreviewAsync(value, CancellationToken.None);

    [RelayCommand(CanExecute = nameof(CanPickFiles))]
    private async Task PickFilesAsync(CancellationToken cancellationToken)
    {
        var paths = await _filePicker.PickFilesAsync(cancellationToken);
        if (paths.Count == 0)
        {
            return;
        }

        IsExtracting = true;
        try
        {
            await ExtractAllAsync(paths, cancellationToken);
        }
        finally
        {
            IsExtracting = false;
            RefreshDerived();
        }
    }

    private bool CanPickFiles() => !IsExtracting;

    [RelayCommand]
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

    [RelayCommand]
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

    private async Task ExtractAllAsync(IReadOnlyList<string> paths, CancellationToken cancellationToken)
    {
        for (var i = 0; i < paths.Count; i++)
        {
            ProgressText = ExtractionStrings.ParsingProgress(i + 1, paths.Count);
            await ExtractOneAsync(paths[i], cancellationToken);
        }
    }

    private async Task ExtractOneAsync(string path, CancellationToken cancellationToken)
    {
        var row = new DocumentRowViewModel(path);
        row.PropertyChanged += OnRowChanged;
        Documents.Add(row);
        RefreshDerived();

        row.Status = DocumentStatus.Extracting;
        var sourceKind = FileClassifier.Classify(path) == FileClassification.Code ? SourceKind.Code : SourceKind.Paper;
        try
        {
            var results = await _extractionService.ExtractAsync([path], SourceType.Local, sourceKind, cancellationToken);
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
        OnPropertyChanged(nameof(FilesCount));
        OnPropertyChanged(nameof(ExtractedCount));
        OnPropertyChanged(nameof(SkippedCount));
        OnPropertyChanged(nameof(TotalUnits));
        OnPropertyChanged(nameof(TotalTokens));
        OnPropertyChanged(nameof(HasSkipped));
        OnPropertyChanged(nameof(AllSkipped));
        OnPropertyChanged(nameof(StatusCaption));
        OnPropertyChanged(nameof(SkipRows));
    }
}
