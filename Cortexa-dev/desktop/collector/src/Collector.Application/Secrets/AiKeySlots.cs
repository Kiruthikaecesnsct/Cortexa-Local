using Collector.Domain.Enums;

namespace Collector.Application.Secrets;

public static class AiKeySlots
{
    public static SecretSlot? For(CollectorProvider provider) => provider switch
    {
        CollectorProvider.Claude => SecretSlot.AnthropicApiKey,
        CollectorProvider.Gemini => SecretSlot.GeminiApiKey,
        _ => null,
    };
}
