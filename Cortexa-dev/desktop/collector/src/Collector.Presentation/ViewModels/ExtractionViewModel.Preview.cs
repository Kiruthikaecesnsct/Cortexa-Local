using Collector.Domain.Enums;
using Collector.Domain.Extraction;
using Collector.Presentation.Behaviors;
using Collector.Presentation.Resources;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;

namespace Collector.Presentation.ViewModels;

public sealed partial class ExtractionViewModel
{
    private CancellationTokenSource? _previewCts;

    public RangeObservableCollection<UnitPreviewItemViewModel> PreviewUnits { get; }

    public bool ShowPreviewPlaceholder => SelectedDocument is null;

    public bool ShowPreviewEmpty => SelectedDocument is not null && SelectedDocument.Status != DocumentStatus.Extracted;

    public bool ShowPreviewUnits => SelectedDocument is not null && SelectedDocument.Status == DocumentStatus.Extracted;

    public bool ShowSectionHint => ShowPreviewUnits && !IsPreviewLoading && SelectedUnit is null;

    public string PreviewHeader => SelectedDocument is null
        ? string.Empty
        : ExtractionStrings.UnitPreviewHeader(SelectedDocument.Filename, SelectedDocument.UnitCount, SelectedDocument.TokenCount);

    public string SectionListName => SelectedDocument is null
        ? string.Empty
        : ExtractionStrings.SectionListName(SelectedDocument.Filename);

    public string SectionDetailName => SelectedUnit is null
        ? string.Empty
        : ExtractionStrings.SectionDetailName(SelectedUnit.Title);

    public string SelectedUnitText => SelectedUnit?.FullText ?? string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(
        nameof(ShowPreviewPlaceholder),
        nameof(ShowPreviewEmpty),
        nameof(ShowPreviewUnits),
        nameof(ShowSectionHint),
        nameof(PreviewHeader),
        nameof(SectionListName))]
    public partial DocumentRowViewModel? SelectedDocument { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedUnitText), nameof(SectionDetailName), nameof(ShowSectionHint))]
    public partial UnitPreviewItemViewModel? SelectedUnit { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowSectionHint))]
    public partial bool IsPreviewLoading { get; set; }

    partial void OnSelectedDocumentChanged(DocumentRowViewModel? value)
    {
        if (!_holdSelection)
        {
            StartPreviewLoad(value);
        }
    }

    private void StartPreviewLoad(DocumentRowViewModel? row)
    {
        CancelPreviewLoad();
        ResetPreview();
        if (row?.DocumentId is null || row.Status != DocumentStatus.Extracted)
        {
            return;
        }

        var source = new CancellationTokenSource();
        _previewCts = source;
        IsPreviewLoading = true;
        _ = LoadPreviewAsync(row, source.Token);
    }

    private void CancelPreviewLoad()
    {
        _previewCts?.Cancel();
        _previewCts?.Dispose();
        _previewCts = null;
        IsPreviewLoading = false;
    }

    private void ResetPreview()
    {
        SelectedUnit = null;
        if (PreviewUnits.Count > 0)
        {
            PreviewUnits.ReplaceAll([]);
        }
    }

    private async Task LoadPreviewAsync(DocumentRowViewModel row, CancellationToken cancellationToken)
    {
        try
        {
            var units = await _unitStore.GetByDocumentIdAsync(row.DocumentId!, cancellationToken);
            ShowPreviewIfCurrent(row, units, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not load extraction units for {DocumentId}.", row.DocumentId);
            FinishPreviewLoad(cancellationToken);
        }
    }

    private void ShowPreviewIfCurrent(DocumentRowViewModel row, IReadOnlyList<ExtractionUnit> units, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested || !ReferenceEquals(row, SelectedDocument))
        {
            return;
        }

        PreviewUnits.ReplaceAll(units.OrderBy(u => u.Ordinal).Select(unit => new UnitPreviewItemViewModel(unit, row.SourcePath)));
        SelectedUnit = PreviewUnits.FirstOrDefault();
        FinishPreviewLoad(cancellationToken);
    }

    private void FinishPreviewLoad(CancellationToken cancellationToken)
    {
        if (!cancellationToken.IsCancellationRequested)
        {
            IsPreviewLoading = false;
        }
    }
}
