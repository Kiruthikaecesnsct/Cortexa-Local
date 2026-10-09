using Collector.Application.Extraction;
using Collector.Domain.Enums;
using Collector.Presentation.Resources;
using Microsoft.Extensions.Logging;

namespace Collector.Presentation.ViewModels;

public sealed partial class ExtractionViewModel
{
    private int _batchDone;

    private void OnFilesFetched(object? sender, RemoteFilesFetchedEventArgs e) => _ = ExtractFetchedAsync(e);

    private async Task ExtractFetchedAsync(RemoteFilesFetchedEventArgs fetched)
    {
        var files = fetched.Files.Select(file => new SplitFile(file.LocalPath, file.RepoPath)).ToList();
        await RunExtractionAsync(new ExtractionBatch(files, fetched.Source, fetched.Origin));
        RequestFocus(ExtractionFocusKeys.Documents);
    }

    private async Task RunExtractionAsync(ExtractionBatch batch)
    {
        var token = Intake.Begin();
        _batchDone = 0;
        IsExtracting = true;
        using var batcher = new DocumentBatcher(_time, outcomes => ApplyBatch(outcomes, batch));
        try
        {
            await SplitAsync(batch, batcher, token);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Splitting was canceled; finished documents were kept.");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Splitting the selected files failed.");
        }
        finally
        {
            batcher.FlushNow();
            Intake.End();
            IsExtracting = false;
            RefreshDerived();
        }
    }

    private Task SplitAsync(ExtractionBatch batch, DocumentBatcher batcher, CancellationToken cancellationToken)
    {
        var request = new SplitRequest(batch.Files, batch.Source);
        var progress = new SplitProgressSink(Intake);
        return Task.Run(() => _splitter.RunAsync(request, progress, batcher.Enqueue, cancellationToken), cancellationToken);
    }

    private void ApplyBatch(IReadOnlyList<SplitOutcome> outcomes, ExtractionBatch batch)
    {
        var rows = outcomes.Select(outcome => new DocumentRowViewModel(outcome, batch.Origin)).ToList();
        AppendPreservingSelection(rows);
        _batchDone += rows.Count;
        ProgressText = ExtractionStrings.ParsingProgress(_batchDone, batch.Files.Count);
        RefreshDerived();
    }

    private void AppendPreservingSelection(List<DocumentRowViewModel> rows)
    {
        PreserveSelection(() =>
        {
            Documents.AddRange(rows);
            rows.ForEach(_tally.Add);
        });
        SelectDefault(rows);
    }

    private void SelectDefault(List<DocumentRowViewModel> rows)
    {
        if (SelectedDocument is not null)
        {
            return;
        }

        SelectedDocument = rows.FirstOrDefault(row => row.Status == DocumentStatus.Extracted && MatchesFilter(row));
    }

    private sealed class SplitProgressSink(IntakeProgressViewModel intake) : IProgress<SplitProgress>
    {
        public void Report(SplitProgress value) => intake.ReportSplit(value.Done, value.Total, value.CurrentPath);
    }
}
