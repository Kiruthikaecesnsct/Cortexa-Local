using Collector.Application.Ports;
using Collector.Domain.Documents;
using Collector.Domain.Extraction;

namespace Collector.Application.Knowledge;

public sealed class ExtractKnowledgeHandler(
    IDocumentStore documentStore,
    IUnitStore unitStore,
    UnitExtractionRunner runner,
    KnowledgeMerger merger,
    KnowledgePrompt prompt,
    IAiProviderFactory providerFactory)
{
    private sealed record Work(CollectorDocument Document, ExtractionUnit Unit);

    public async Task<ExtractionRunResult> ExtractAsync(
        ExtractionRunRequest request,
        IProgress<ExtractionProgress>? progress,
        CancellationToken cancellationToken)
    {
        var work = await LoadWorkAsync(request.DocumentIds, cancellationToken);
        var context = new ExtractionRunContext(request.Provider, request.Model);
        var outcomes = await RunAsync(work, context, progress, cancellationToken);
        return BuildResult(request, work, outcomes);
    }

    private async Task<List<Work>> LoadWorkAsync(IReadOnlyList<string> documentIds, CancellationToken cancellationToken)
    {
        var work = new List<Work>();
        foreach (var documentId in documentIds.Distinct(StringComparer.Ordinal))
        {
            var document = await documentStore.GetAsync(documentId, cancellationToken);
            if (document is null)
            {
                continue;
            }

            var units = await unitStore.GetByDocumentIdAsync(documentId, cancellationToken);
            work.AddRange(units.OrderBy(unit => unit.Ordinal).Select(unit => new Work(document, unit)));
        }

        return work;
    }

    private async Task<UnitOutcome[]> RunAsync(
        List<Work> work,
        ExtractionRunContext context,
        IProgress<ExtractionProgress>? progress,
        CancellationToken cancellationToken)
    {
        var outcomes = new UnitOutcome[work.Count];
        var completed = 0;
        var parallel = new ParallelOptions
        {
            MaxDegreeOfParallelism = Math.Max(1, providerFactory.ConcurrencyFor(context.Provider)),
            CancellationToken = cancellationToken,
        };

        await Parallel.ForEachAsync(Enumerable.Range(0, work.Count), parallel, async (index, token) =>
        {
            outcomes[index] = await runner.ExtractAsync(work[index].Unit, work[index].Document, context, token);
            progress?.Report(new ExtractionProgress(Interlocked.Increment(ref completed), work.Count));
        });
        return outcomes;
    }

    private ExtractionRunResult BuildResult(ExtractionRunRequest request, List<Work> work, UnitOutcome[] outcomes) => new()
    {
        Items = MergeItems(outcomes),
        TotalUnits = work.Count,
        FailedUnits = outcomes.Count(outcome => outcome.Status == UnitOutcomeStatus.Failed),
        SkippedUnits = outcomes.Count(outcome => outcome.Status == UnitOutcomeStatus.Skipped),
        FailedDocumentIds = FailedDocuments(work, outcomes),
        Provider = request.Provider,
        Model = outcomes.Select(outcome => outcome.Model).FirstOrDefault(model => model is not null) ?? string.Empty,
        PromptVersion = prompt.Version,
    };

    private List<ExtractedKnowledgeItem> MergeItems(UnitOutcome[] outcomes) =>
    [
        .. outcomes
            .SelectMany(outcome => outcome.Items)
            .GroupBy(item => item.DocumentId)
            .SelectMany(group => merger.Merge(group)),
    ];

    private static List<string> FailedDocuments(List<Work> work, UnitOutcome[] outcomes) =>
    [
        .. work.Select((item, index) => (item.Document.Id, outcomes[index].Status))
            .GroupBy(pair => pair.Id)
            .Where(group => group.All(pair => pair.Status == UnitOutcomeStatus.Failed))
            .Select(group => group.Key),
    ];
}
