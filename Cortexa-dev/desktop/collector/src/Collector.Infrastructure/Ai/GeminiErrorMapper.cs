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

    public static AiProviderException Map(ApiException exception) =>
        MapStatus((HttpStatusCode)exception.StatusCode, exception);

    public static AiProviderException MapUnreachable(HttpRequestException exception) =>
        exception.StatusCode is { } status
            ? MapStatus(status, exception)
            : new AiProviderException(AiFailureKind.Transient, "The Gemini service could not be reached.", exception);

    public static AiProviderException MapTimeout(Exception exception) =>
        new(AiFailureKind.Transient, "The Gemini request timed out.", exception);

    public static AiProviderException MapMalformed(Exception exception) =>
        new(AiFailureKind.Transient, "Gemini returned a response that could not be read.", exception);

    private static AiProviderException MapStatus(HttpStatusCode status, Exception exception) =>
        new(KindFor(status), Describe(status), exception);

    private static AiFailureKind KindFor(HttpStatusCode status)
    {
        if (PermanentStatuses.Contains(status))
        {
            return AiFailureKind.Permanent;
        }

        return status == HttpStatusCode.TooManyRequests ? AiFailureKind.QuotaExceeded : AiFailureKind.Transient;
    }

    private static string Describe(HttpStatusCode status) => status switch
    {
        HttpStatusCode.BadRequest => "Gemini rejected the request.",
        HttpStatusCode.Unauthorized => "Gemini rejected the API key.",
        HttpStatusCode.Forbidden => "Gemini denied access for this API key.",
        HttpStatusCode.TooManyRequests => "Gemini's rate limit or quota was exceeded.",
        _ => $"Gemini returned status {(int)status}.",
    };
}
