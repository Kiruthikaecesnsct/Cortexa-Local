using Amazon.BedrockRuntime;
using Amazon.BedrockRuntime.Model;
using Collector.Application.Ports;
using Collector.Infrastructure.Ai;
using Collector.Infrastructure.Options;

namespace Collector.Tests.Ai;

public sealed class BedrockRequestFactoryTests
{
    private const string ConfiguredModel = "us.anthropic.claude-sonnet-4-5-20250929-v1:0";
    private const string RequestedModel = "us.anthropic.claude-opus-4-1";
    private const string SystemText = "You are a patent analyst.";
    private const string UserText = "Summarize the attached claims.";
    private const int ConfiguredMaxOutputTokens = 4096;
    private const double ConfiguredTemperature = 0.25;

    private static AiRequest Request(string? model = null) => new()
    {
        SystemText = SystemText,
        UserText = UserText,
        OutputSchemaJson = "{}",
        Model = model,
    };

    private static BedrockProviderOptions Options() => new()
    {
        Model = ConfiguredModel,
        MaxOutputTokens = ConfiguredMaxOutputTokens,
        Temperature = ConfiguredTemperature,
    };

    [Fact]
    public void Create_BuildsSystemBlockFromRequestContent()
    {
        var call = BedrockRequestFactory.Create(Request(), Options());

        var systemBlock = Assert.Single(call.System);
        Assert.Equal(SystemText, systemBlock.Text);
    }

    [Fact]
    public void Create_BuildsUserMessageFromRequestContent()
    {
        var call = BedrockRequestFactory.Create(Request(), Options());

        var message = Assert.Single(call.Messages);
        Assert.Equal(ConversationRole.User, message.Role);
        var content = Assert.Single(message.Content);
        Assert.Equal(UserText, content.Text);
    }

    [Fact]
    public void Create_NoRequestedModel_FallsBackToConfiguredModel()
    {
        var call = BedrockRequestFactory.Create(Request(), Options());

        Assert.Equal(ConfiguredModel, call.ModelId);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_BlankRequestedModel_FallsBackToConfiguredModel(string? requestedModel)
    {
        var call = BedrockRequestFactory.Create(Request(requestedModel), Options());

        Assert.Equal(ConfiguredModel, call.ModelId);
    }

    [Fact]
    public void Create_RequestedModelProvided_OverridesConfiguredModel()
    {
        var call = BedrockRequestFactory.Create(Request(RequestedModel), Options());

        Assert.Equal(RequestedModel, call.ModelId);
    }

    [Fact]
    public void Create_InferenceConfig_AppliesMaxOutputTokensAndTemperature()
    {
        var call = BedrockRequestFactory.Create(Request(), Options());

        Assert.Equal(ConfiguredMaxOutputTokens, call.InferenceConfig.MaxTokens);
        Assert.Equal((float)ConfiguredTemperature, call.InferenceConfig.Temperature);
    }

    [Fact]
    public void Create_NoTemperatureConfigured_LeavesTemperatureNull()
    {
        var settings = Options();
        settings.Temperature = null;

        var call = BedrockRequestFactory.Create(Request(), settings);

        Assert.Null(call.InferenceConfig.Temperature);
    }
}
