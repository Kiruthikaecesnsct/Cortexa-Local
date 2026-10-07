using System.Net;
using Collector.Application.Ports;
using Google.GenAI;

namespace Collector.Infrastructure.Ai;

public static class GeminiErrorMapper
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

    public static AiProviderException Map(ApiException exception)
    {
        var status = (HttpStatusCode)exception.StatusCode;
        var kind = PermanentStatuses.Contains(status) ? AiFailureKind.Permanent : AiFailureKind.Transient;
        return new AiProviderException(kind, Describe(status), exception);
    }

    public static AiProviderException MapUnreachable(Exception exception) =>
        new(AiFailureKind.Transient, "The Gemini service could not be reached.", exception);

    public static AiProviderException MapTimeout(Exception exception) =>
        new(AiFailureKind.Transient, "The Gemini request timed out.", exception);

    public static AiProviderException MapMalformed(Exception exception) =>
        new(AiFailureKind.Transient, "Gemini returned a response that could not be read.", exception);

    private static string Describe(HttpStatusCode status) => status switch
    {
        HttpStatusCode.BadRequest => "Gemini rejected the request.",
        HttpStatusCode.Unauthorized => "Gemini rejected the API key.",
        HttpStatusCode.Forbidden => "Gemini denied access for this API key.",
        HttpStatusCode.TooManyRequests => "Gemini is rate limiting requests.",
        _ => $"Gemini returned status {(int)status}.",
    };
}
