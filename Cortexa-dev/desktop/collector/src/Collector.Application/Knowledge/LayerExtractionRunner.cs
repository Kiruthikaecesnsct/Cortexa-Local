using Collector.Application.Ports;
using Microsoft.Extensions.Logging;

namespace Collector.Application.Knowledge;

public sealed class LayerExtractionRunner(
    IAiProviderFactory providerFactory,
    LayerPromptBuilder promptBuilder,
    LayerKnowledgeParser parser,
    LayerItemAssembler assembler,
    ILogger<LayerExtractionRunner> logger)
{
    public async Task<IReadOnlyList<ExtractedKnowledgeItem>> ExtractAsync(
        FolderGroup group,
        ExtractionRunContext context,
        CancellationToken cancellationToken)
    {
        AiCompletion completion;
        try
        {
            var provider = providerFactory.Resolve(context.Provider);
            var request = promptBuilder.Build(group) with { Model = context.Model };
            completion = await provider.CompleteAsync(request, cancellationToken);
        }
        catch (AiProviderException exception) when (exception.Kind != AiFailureKind.MissingApiKey)
        {
            logger.LogWarning("Folder {Folder} failed with a {Kind} provider error.", group.FolderPath, exception.Kind);
            return [];
        }

        return completion.Outcome switch
        {
            AiOutcome.Refused => SkipRefused(group),
            AiOutcome.Truncated => SkipTruncated(group),
            _ => ParseCompletion(group, completion),
        };
    }

    private IReadOnlyList<ExtractedKnowledgeItem> SkipRefused(FolderGroup group)
    {
        logger.LogInformation("Folder {Folder} was refused by the model and skipped.", group.FolderPath);
        return [];
    }

    private IReadOnlyList<ExtractedKnowledgeItem> SkipTruncated(FolderGroup group)
    {
        logger.LogWarning("Folder {Folder} was cut off by the model and skipped; layer folders are not split.", group.FolderPath);
        return [];
    }

    private IReadOnlyList<ExtractedKnowledgeItem> ParseCompletion(FolderGroup group, AiCompletion completion)
    {
        var parsed = parser.Parse(completion.Text);
        if (!parsed.Succeeded)
        {
            logger.LogWarning("Folder {Folder} returned a response that could not be parsed.", group.FolderPath);
            return [];
        }

        if (parsed.DroppedItems > 0)
        {
            logger.LogInformation("Folder {Folder}: dropped {Count} unusable items.", group.FolderPath, parsed.DroppedItems);
        }

        return [.. parsed.Items.Select(raw => assembler.Assemble(group, raw))];
    }
}
