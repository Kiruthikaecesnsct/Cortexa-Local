using Collector.Application.Ports;
using Collector.Domain.Documents;
using Collector.Domain.Extraction;
using Microsoft.Extensions.Logging;

namespace Collector.Application.Knowledge;

public sealed class UnitExtractionRunner(
    IAiProvider provider,
    KnowledgePromptBuilder promptBuilder,
    KnowledgeParser parser,
    UnitItemAssembler assembler,
    UnitSplitter splitter,
    ILogger<UnitExtractionRunner> logger)
{
    public const int MaxSplitDepth = 2;

    public Task<UnitOutcome> ExtractAsync(ExtractionUnit unit, CollectorDocument document, CancellationToken cancellationToken) =>
        ExtractAsync(unit, document, 0, cancellationToken);

    private async Task<UnitOutcome> ExtractAsync(
        ExtractionUnit unit,
        CollectorDocument document,
        int depth,
        CancellationToken cancellationToken)
    {
        AiCompletion completion;
        try
        {
            completion = await provider.CompleteAsync(promptBuilder.Build(unit), cancellationToken);
        }
        catch (AiProviderException exception) when (exception.Kind != AiFailureKind.MissingApiKey)
        {
            logger.LogWarning("Unit {UnitId} failed with a {Kind} provider error.", unit.Id, exception.Kind);
            return UnitOutcome.Failed;
        }

        return completion.Outcome switch
        {
            AiOutcome.Refused => SkipRefused(unit, completion),
            AiOutcome.Truncated => await SplitAsync(unit, document, depth, cancellationToken),
            _ => ParseCompletion(unit, document, completion),
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

    private async Task<UnitOutcome> SplitAsync(
        ExtractionUnit unit,
        CollectorDocument document,
        int depth,
        CancellationToken cancellationToken)
    {
        var halves = depth < MaxSplitDepth ? splitter.Split(unit) : null;
        if (halves is null)
        {
            logger.LogWarning("Unit {UnitId} was cut off and cannot be split further.", unit.Id);
            return UnitOutcome.Failed;
        }

        var first = await ExtractAsync(halves.Value.First, document, depth + 1, cancellationToken);
        var second = await ExtractAsync(halves.Value.Second, document, depth + 1, cancellationToken);
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
