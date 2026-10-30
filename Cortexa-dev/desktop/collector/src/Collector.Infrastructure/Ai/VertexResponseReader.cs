using System.Text;
using Collector.Application.Ports;

namespace Collector.Infrastructure.Ai;

internal static class VertexResponseReader
{
    private const string MaxTokensFinishReason = "MAX_TOKENS";

    private static readonly HashSet<string> RefusalFinishReasons = new(StringComparer.OrdinalIgnoreCase)
    {
        "SAFETY",
        "RECITATION",
        "BLOCKLIST",
        "PROHIBITED_CONTENT",
        "SPII",
        "LANGUAGE",
    };

    public static AiCompletion ToCompletion(VertexGenerateContentResponseWire response, string configuredModel)
    {
        var candidate = response.Candidates?.FirstOrDefault();
        return new AiCompletion
        {
            Text = JoinText(candidate),
            Outcome = ToOutcome(candidate?.FinishReason),
            Model = string.IsNullOrWhiteSpace(response.ModelVersion) ? configuredModel : response.ModelVersion,
        };
    }

    private static string JoinText(VertexCandidateWire? candidate)
    {
        var builder = new StringBuilder();
        foreach (var part in candidate?.Content?.Parts ?? [])
        {
            builder.Append(part.Text);
        }

        return builder.ToString();
    }

    private static AiOutcome ToOutcome(string? finishReason)
    {
        if (string.Equals(finishReason, MaxTokensFinishReason, StringComparison.OrdinalIgnoreCase))
        {
            return AiOutcome.Truncated;
        }

        return finishReason is not null && RefusalFinishReasons.Contains(finishReason)
            ? AiOutcome.Refused
            : AiOutcome.Completed;
    }
}
