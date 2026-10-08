using Collector.Application.Ports;
using Collector.Domain.Documents;
using Collector.Domain.Enums;
using Collector.Domain.Extraction;
using Microsoft.Extensions.Logging;

namespace Collector.Application.Knowledge;

public sealed record ExtractionRunContext(CollectorProvider Provider, string? Model);

public sealed class UnitExtractionRunner(
    IAiProviderFactory providerFactory,
    KnowledgePromptBuilder promptBuilder,
    KnowledgeParser parser,
    UnitItemAssembler assembler,
    UnitSplitter splitter,
    ILogger<UnitExtractionRunner> logger)
{
    public const int MaxSplitDepth = 2;

    private readonly record struct State(ExtractionUnit Unit, CollectorDocument Document, ExtractionRunContext Context, int Depth);

    public Task<UnitOutcome> ExtractAsync(
        ExtractionUnit unit,
        CollectorDocument document,
        ExtractionRunContext context,
        CancellationToken cancellationToken) =>
        ExtractAsync(new State(unit, document, context, 0), cancellationToken);

    private async Task<UnitOutcome> ExtractAsync(State state, CancellationToken cancellationToken)
    {
        AiCompletion completion;
        try
        {
            var provider = providerFactory.Resolve(state.Context.Provider);
            var request = promptBuilder.Build(state.Unit) with { Model = state.Context.Model };
            completion = await provider.CompleteAsync(request, cancellationToken);
        }
        catch (AiProviderException exception) when (exception.Kind != AiFailureKind.MissingApiKey)
        {
            logger.LogWarning("Unit {UnitId} failed with a {Kind} provider error.", state.Unit.Id, exception.Kind);
            return UnitOutcome.Failed;
        }

        return completion.Outcome switch
        {
            AiOutcome.Refused => SkipRefused(state.Unit, completion),
            AiOutcome.Truncated => await SplitAsync(state, cancellationToken),
            _ => ParseCompletion(state.Unit, state.Document, completion),
        };
    }

    private UnitOutcome SkipRefused(ExtractionUnit unit, AiCompletion completion)
    {
        logger.LogInformation("Unit {UnitId} was refused by the model and skipped.", unit.Id);
        return UnitOutcome.Skipped(completion.Model);
    }

    private UnitOutcome ParseCompletion(ExtractionUnit unit, CollectorDocument document, AiCompletion completion)
    {
        var parsed = parser.Parse(completion.Text);
        if (!parsed.Succeeded)
        {
            logger.LogWarning("Unit {UnitId} returned a response that could not be parsed.", unit.Id);
            return UnitOutcome.Failed;
        }

        if (parsed.DroppedItems > 0)
        {
            logger.LogInformation("Unit {UnitId}: dropped {Count} unusable items.", unit.Id, parsed.DroppedItems);
        }

        var items = parsed.Items.Select(raw => assembler.Assemble(unit, document, raw)).ToList();
        return UnitOutcome.Completed(items, completion.Model);
    }

    private async Task<UnitOutcome> SplitAsync(State state, CancellationToken cancellationToken)
    {
        var halves = state.Depth < MaxSplitDepth ? splitter.Split(state.Unit) : null;
        if (halves is null)
        {
            logger.LogWarning("Unit {UnitId} was cut off and cannot be split further.", state.Unit.Id);
            return UnitOutcome.Failed;
        }

        var first = await ExtractAsync(state with { Unit = halves.Value.First, Depth = state.Depth + 1 }, cancellationToken);
        var second = await ExtractAsync(state with { Unit = halves.Value.Second, Depth = state.Depth + 1 }, cancellationToken);
        return Combine(first, second);
    }

    private static UnitOutcome Combine(UnitOutcome first, UnitOutcome second)
    {
        if (first.Status == UnitOutcomeStatus.Failed || second.Status == UnitOutcomeStatus.Failed)
        {
            return UnitOutcome.Failed;
        }

        var model = first.Model ?? second.Model;
        if (first.Status == UnitOutcomeStatus.Skipped && second.Status == UnitOutcomeStatus.Skipped)
        {
            return UnitOutcome.Skipped(model);
        }

        return UnitOutcome.Completed([.. first.Items, .. second.Items], model);
    }
}
