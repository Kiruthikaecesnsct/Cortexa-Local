using System.Text;
using Amazon.BedrockRuntime;
using Amazon.BedrockRuntime.Model;
using Amazon.Runtime;
using Amazon.SSO;
using Amazon.SSOOIDC;
using Collector.Application.Ports;
using Collector.Infrastructure.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Collector.Infrastructure.Ai;

public sealed class BedrockDirectProvider(
    BedrockClientFactory clientFactory,
    IOptions<BedrockProviderOptions> options,
    ILogger<BedrockDirectProvider> logger) : IAiProvider
{
    public async Task<AiCompletion> CompleteAsync(AiRequest request, CancellationToken cancellationToken)
    {
        var client = await clientFactory.CreateAsync(cancellationToken).ConfigureAwait(false);
        var call = BedrockRequestFactory.Create(request, options.Value);
        var response = await SendAsync(client, call, cancellationToken).ConfigureAwait(false);
        LogUsage(response);
        return ToCompletion(response, call.ModelId);
    }

    private static async Task<ConverseResponse> SendAsync(
        IAmazonBedrockRuntime client,
        ConverseRequest call,
        CancellationToken cancellationToken)
    {
        try
        {
            return await client.ConverseAsync(call, cancellationToken).ConfigureAwait(false);
        }
        catch (AmazonBedrockRuntimeException exception)
        {
            throw BedrockErrorMapper.MapBedrock(exception);
        }
        catch (AmazonSSOOIDCException exception)
        {
            throw BedrockErrorMapper.MapIdentityCenter(exception);
        }
        catch (AmazonSSOException exception)
        {
            throw BedrockErrorMapper.MapIdentityCenter(exception);
        }
        catch (AmazonServiceException exception)
        {
            throw BedrockErrorMapper.MapUnreachable(exception);
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw BedrockErrorMapper.MapTimeout(exception);
        }
    }

    private static AiCompletion ToCompletion(ConverseResponse response, string modelId) => new()
    {
        Text = JoinText(response.Output?.Message),
        Outcome = ToOutcome(response.StopReason),
        Model = modelId,
    };

    private static string JoinText(Message? message)
    {
        if (message is null)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        foreach (var block in message.Content)
        {
            if (!string.IsNullOrEmpty(block.Text))
            {
                builder.Append(block.Text);
            }
        }

        return builder.ToString();
    }

    private static AiOutcome ToOutcome(StopReason? reason)
    {
        if (reason == StopReason.Max_tokens)
        {
            return AiOutcome.Truncated;
        }

        if (reason == StopReason.Content_filtered || reason == StopReason.Guardrail_intervened)
        {
            return AiOutcome.Refused;
        }

        return AiOutcome.Completed;
    }

    private void LogUsage(ConverseResponse response) =>
        logger.LogDebug(
            "Bedrock call used {InputTokens} input and {OutputTokens} output tokens.",
            response.Usage?.InputTokens,
            response.Usage?.OutputTokens);
}
