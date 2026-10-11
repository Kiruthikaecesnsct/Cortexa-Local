using Collector.Application.Ports;

namespace Collector.Infrastructure.Ai;

internal static class GroqResponseReader
{
    private const string LengthFinishReason = "length";

    private const string ContentFilterFinishReason = "content_filter";

    public static AiCompletion ToCompletion(GroqChatResponseWire response, string configuredModel)
    {
        var choice = response.Choices?.Count > 0 ? response.Choices[0] : null;
        return new AiCompletion
        {
            Text = choice?.Message?.Content ?? string.Empty,
            Outcome = ToOutcome(choice?.FinishReason),
            Model = string.IsNullOrWhiteSpace(response.Model) ? configuredModel : response.Model,
        };
    }

    private static AiOutcome ToOutcome(string? finishReason) => finishReason switch
    {
        LengthFinishReason => AiOutcome.Truncated,
        ContentFilterFinishReason => AiOutcome.Refused,
        _ => AiOutcome.Completed,
    };
}
