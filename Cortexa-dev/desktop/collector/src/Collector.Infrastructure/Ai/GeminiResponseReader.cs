using System.Text;
using Collector.Application.Ports;
using Google.GenAI.Types;

namespace Collector.Infrastructure.Ai;

public static class GeminiResponseReader
{
    private static readonly HashSet<FinishReason> RefusalReasons =
    [
        FinishReason.Safety,
        FinishReason.Recitation,
        FinishReason.Blocklist,
        FinishReason.ProhibitedContent,
        FinishReason.Spii,
        FinishReason.Language,
    ];

    public static AiCompletion ToCompletion(GenerateContentResponse response, string configuredModel)
    {
        var candidate = response.Candidates?.FirstOrDefault();
        return new AiCompletion
        {
            Text = JoinText(candidate),
            Outcome = ToOutcome(response, candidate),
            Model = string.IsNullOrWhiteSpace(response.ModelVersion) ? configuredModel : response.ModelVersion,
        };
    }

    private static string JoinText(Candidate? candidate)
    {
        var builder = new StringBuilder();
        foreach (var part in candidate?.Content?.Parts ?? [])
        {
            if (part.Thought != true && part.Text is not null)
            {
                builder.Append(part.Text);
            }
        }

        return builder.ToString();
    }

    private static AiOutcome ToOutcome(GenerateContentResponse response, Candidate? candidate)
    {
        if (candidate is null)
        {
            return response.PromptFeedback?.BlockReason is null ? AiOutcome.Completed : AiOutcome.Refused;
        }

        if (candidate.FinishReason is not { } reason)
        {
            return AiOutcome.Completed;
        }

        if (reason == FinishReason.MaxTokens)
        {
            return AiOutcome.Truncated;
        }

        return RefusalReasons.Contains(reason) ? AiOutcome.Refused : AiOutcome.Completed;
    }
}
