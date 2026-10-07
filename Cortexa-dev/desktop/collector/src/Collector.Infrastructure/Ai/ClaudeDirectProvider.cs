using System.Text;
using Anthropic.Exceptions;
using Anthropic.Models.Messages;
using Collector.Application.Ports;
using Collector.Infrastructure.Options;
using Microsoft.Extensions.Options;

namespace Collector.Infrastructure.Ai;

public sealed class ClaudeDirectProvider(
    AnthropicClientFactory clientFactory,
    IOptions<AiProviderOptions> options) : IAiProvider
{
    public async Task<AiCompletion> CompleteAsync(AiRequest request, CancellationToken cancellationToken)
    {
        var client = await clientFactory.CreateAsync(cancellationToken);
        var parameters = ClaudeRequestFactory.Create(request, options.Value);
        try
        {
            var message = await client.Messages.Create(parameters, cancellationToken);
            return ToCompletion(message);
        }
        catch (AnthropicException exception)
        {
            throw AnthropicErrorMapper.Map(exception);
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw AnthropicErrorMapper.MapTimeout(exception);
        }
    }

    private AiCompletion ToCompletion(Message message) => new()
    {
        Text = JoinText(message),
        Outcome = ToOutcome(message.StopReason?.Value()),
        Model = message.Model.Raw(),
    };

    private static string JoinText(Message message)
    {
        var builder = new StringBuilder();
        foreach (var block in message.Content)
        {
            if (block.TryPickText(out var text))
            {
                builder.Append(text.Text);
            }
        }

        return builder.ToString();
    }

    private static AiOutcome ToOutcome(StopReason? reason) => reason switch
    {
        StopReason.Refusal => AiOutcome.Refused,
        StopReason.MaxTokens => AiOutcome.Truncated,
        _ => AiOutcome.Completed,
    };
}
