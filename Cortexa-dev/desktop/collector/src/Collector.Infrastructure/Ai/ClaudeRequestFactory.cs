using System.Text.Json;
using Anthropic.Models.Messages;
using Collector.Application.Ports;
using Collector.Infrastructure.Options;

namespace Collector.Infrastructure.Ai;

internal static class ClaudeRequestFactory
{
    public static MessageCreateParams Create(AiRequest request, AiProviderOptions settings) => new()
    {
        Model = settings.Model,
        MaxTokens = settings.MaxOutputTokens,
        System = new List<TextBlockParam> { CachedSystemBlock(request.SystemText) },
        Messages = [new MessageParam { Role = Role.User, Content = request.UserText }],
        OutputConfig = BuildOutputConfig(request.OutputSchemaJson, settings),
        Thinking = BuildThinking(settings.Thinking),
#pragma warning disable CS0618
        Temperature = settings.Temperature,
#pragma warning restore CS0618
    };

    private static TextBlockParam CachedSystemBlock(string text) => new()
    {
        Text = text,
        CacheControl = new CacheControlEphemeral(),
    };

    private static OutputConfig BuildOutputConfig(string schemaJson, AiProviderOptions settings)
    {
        var schema = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(schemaJson)
            ?? throw new ArgumentException("Output schema must be a JSON object.", nameof(schemaJson));
        return new OutputConfig
        {
            Effort = Enum.Parse<Effort>(settings.Effort, ignoreCase: true),
            Format = new JsonOutputFormat { Schema = schema },
        };
    }

    private static ThinkingConfigParam? BuildThinking(string mode) => mode switch
    {
        AiProviderOptions.ThinkingBetweenTools => new ThinkingConfigBetweenTools(),
        AiProviderOptions.ThinkingAdaptive => new ThinkingConfigAdaptive(),
        AiProviderOptions.ThinkingDisabled => new ThinkingConfigDisabled(),
        _ => null,
    };
}
