using System.Net;
using Anthropic.Exceptions;
using Collector.Application.Ports;

namespace Collector.Infrastructure.Ai;

internal static class AnthropicErrorMapper
{
    private static readonly HashSet<HttpStatusCode> PermanentStatuses =
    [
        HttpStatusCode.BadRequest,
        HttpStatusCode.Unauthorized,
        HttpStatusCode.Forbidden,
        HttpStatusCode.NotFound,
        HttpStatusCode.RequestEntityTooLarge,
        HttpStatusCode.UnprocessableEntity,
    ];

    public static AiProviderException Map(AnthropicException exception)
    {
        if (exception is not AnthropicApiException api)
        {
            return new AiProviderException(AiFailureKind.Transient, "The Claude service could not be reached.", exception);
        }

        var permanent = PermanentStatuses.Contains(api.StatusCode);
        var kind = permanent ? AiFailureKind.Permanent : AiFailureKind.Transient;
        return new AiProviderException(kind, Describe(api.StatusCode), exception);
    }

    public static AiProviderException MapTimeout(Exception exception) =>
        new(AiFailureKind.Transient, "The Claude request timed out.", exception);

    private static string Describe(HttpStatusCode status) => status switch
    {
        HttpStatusCode.Unauthorized => "Claude rejected the API key.",
        HttpStatusCode.Forbidden => "Claude denied access for this API key.",
        HttpStatusCode.TooManyRequests => "Claude is rate limiting requests.",
        _ => $"Claude returned status {(int)status}.",
    };
}
