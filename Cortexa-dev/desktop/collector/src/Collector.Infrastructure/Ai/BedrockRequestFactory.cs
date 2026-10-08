using Amazon.BedrockRuntime;
using Amazon.BedrockRuntime.Model;
using Collector.Application.Ports;
using Collector.Infrastructure.Options;

namespace Collector.Infrastructure.Ai;

public static class BedrockRequestFactory
{
    public static ConverseRequest Create(AiRequest request, BedrockProviderOptions settings) => new()
    {
        ModelId = string.IsNullOrWhiteSpace(request.Model) ? settings.Model : request.Model,
        System = [new SystemContentBlock { Text = request.SystemText }],
        Messages = [new Message { Role = ConversationRole.User, Content = [new ContentBlock { Text = request.UserText }] }],
        InferenceConfig = BuildInferenceConfig(settings),
    };

    private static InferenceConfiguration BuildInferenceConfig(BedrockProviderOptions settings) => new()
    {
        MaxTokens = settings.MaxOutputTokens,
        Temperature = settings.Temperature.HasValue ? (float)settings.Temperature.Value : null,
    };
}
