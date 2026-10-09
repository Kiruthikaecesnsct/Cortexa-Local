using Collector.Application.Knowledge;
using Collector.Domain.Enums;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Collector.Application.Extraction;

public sealed record SplitFile(string Path, string? RepoPath = null);

public sealed record SplitRequest(IReadOnlyList<SplitFile> Files, SourceType Source);

public sealed record SplitProgress(int Done, int Total, string CurrentPath);

public sealed class ParallelSplitter(
    ExtractionService extractionService,
    TokenEstimator tokenEstimator,
    IOptions<ExtractionOptions> options,
    ILogger<ParallelSplitter> logger)
{
    public async Task RunAsync(
        SplitRequest request,
        IProgress<SplitProgress>? progress,
        Action<SplitOutcome> onOutcome,
        CancellationToken cancellationToken)
    {
        var files = DistinctFiles(request.Files);
        var done = 0;
        var parallel = new ParallelOptions
        {
            MaxDegreeOfParallelism = Math.Max(1, options.Value.MaxParallelSplits),
            CancellationToken = cancellationToken,
        };

        await Parallel.ForEachAsync(files, parallel, async (file, token) =>
        {
            var outcome = await SplitOneAsync(file, request.Source, token);
            onOutcome(outcome);
            progress?.Report(new SplitProgress(Interlocked.Increment(ref done), files.Count, file.Path));
        });
    }

    private static List<SplitFile> DistinctFiles(IReadOnlyList<SplitFile> files) =>
        [.. files.DistinctBy(file => file.Path, StringComparer.OrdinalIgnoreCase)];

    private static SourceKind KindOf(string path) =>
        FileClassifier.Classify(path) == FileClassification.Code ? SourceKind.Code : SourceKind.Paper;

    private async Task<SplitOutcome> SplitOneAsync(SplitFile file, SourceType source, CancellationToken cancellationToken)
    {
        try
        {
            var extracted = await extractionService.ExtractFileAsync(file.Path, source, KindOf(file.Path), cancellationToken);
            return Summarize(file, extracted);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Unexpected failure splitting {FilePath}.", file.Path);
            var failed = new ExtractionResult { SourcePath = file.Path, Status = DocumentStatus.Failed, Reason = ex.Message };
            return new SplitOutcome(failed, file.RepoPath, 0, 0, 0);
        }
    }

    private SplitOutcome Summarize(SplitFile file, ExtractedFile extracted)
    {
        var units = extracted.Units;
        return new SplitOutcome(
            extracted.Result,
            file.RepoPath,
            units.Count,
            units.Sum(unit => unit.TokenCount),
            tokenEstimator.CountPromptTokens(units));
    }
}
