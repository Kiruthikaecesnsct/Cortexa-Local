using Collector.Application.Ports;
using Collector.Application.Settings;
using Collector.Domain.Documents;
using Collector.Domain.Enums;

namespace Collector.Application.Candidates;

public sealed class IdentifyCandidatesHandler(
    IDocumentStore documentStore,
    IAiProviderFactory providerFactory,
    DocumentCandidateRunner runner,
    IGeminiModelExhaustion exhaustion,
    AiModelChoiceService modelChoice)
{
    private const string NotFoundReason = "document not found";

    public async Task<CandidateRunResult> IdentifyAsync(
        CandidateRunRequest request,
        IProgress<CandidateProgress>? progress,
        CancellationToken cancellationToken)
    {
        exhaustion.Reset();
        var choice = modelChoice.Current;
        var context = new CandidateRunContext(choice.Provider, choice.Model);
        var ids = request.DistinctDocumentIds;
        var results = ids.Select(id => CandidateDocumentResult.Canceled(id, id)).ToArray();
        var state = new RunState(ids.Count, progress);
        try
        {
            var documents = await LoadAsync(ids, results, state, cancellationToken);
            await RunAllAsync(documents, results, new RunScope(context, state), cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }

        return new CandidateRunResult(results, context.Provider, state.Model ?? context.Model);
    }

    private async Task<CollectorDocument?[]> LoadAsync(
        IReadOnlyList<string> ids,
        CandidateDocumentResult[] results,
        RunState state,
        CancellationToken cancellationToken)
    {
        var documents = new CollectorDocument?[ids.Count];
        for (var index = 0; index < ids.Count; index++)
        {
            documents[index] = await documentStore.GetAsync(ids[index], cancellationToken);
            if (documents[index] is null)
            {
                results[index] = CandidateDocumentResult.Failure(ids[index], ids[index], CandidateOutcome.Failed, NotFoundReason);
                state.Advance(index);
            }
        }

        return documents;
    }

    private async Task RunAllAsync(
        CollectorDocument?[] documents,
        CandidateDocumentResult[] results,
        RunScope scope,
        CancellationToken cancellationToken)
    {
        var state = scope.State;
        var options = new ParallelOptions
        {
            MaxDegreeOfParallelism = Math.Max(1, providerFactory.ConcurrencyFor(scope.Context.Provider)),
            CancellationToken = cancellationToken,
        };
        var indexes = Enumerable.Range(0, documents.Length).Where(index => documents[index] is not null);
        await Parallel.ForEachAsync(indexes, options, async (index, token) =>
        {
            void ReportStage(CandidateStage stage, double fraction) => state.ReportStage(index, stage, fraction);
            var run = await runner.RunAsync(documents[index]!, scope.Context, ReportStage, token);
            results[index] = run.Result;
            state.Record(index, run);
        });
    }

    private sealed record RunScope(CandidateRunContext Context, RunState State);

    private sealed class RunState(int total, IProgress<CandidateProgress>? progress)
    {
        private readonly Lock _gate = new();
        private int _completed;
        private string? _model;

        public string? Model => Volatile.Read(ref _model);

        public void Record(int documentIndex, DocumentRun run)
        {
            if (run.Model is not null)
            {
                Interlocked.CompareExchange(ref _model, run.Model, null);
            }

            Advance(documentIndex);
        }

        public void Advance(int documentIndex)
        {
            lock (_gate)
            {
                var completed = ++_completed;
                progress?.Report(new CandidateProgress(completed, total, documentIndex, CandidateStage.Save, 1d));
            }
        }

        public void ReportStage(int documentIndex, CandidateStage stage, double stageFraction)
        {
            lock (_gate)
            {
                progress?.Report(new CandidateProgress(_completed, total, documentIndex, stage, stageFraction));
            }
        }
    }
}
