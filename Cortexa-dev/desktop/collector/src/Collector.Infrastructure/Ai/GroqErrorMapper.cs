using System.Net;
using Collector.Application.Ports;

namespace Collector.Infrastructure.Ai;

internal static class GroqErrorMapper
{
    private static readonly HashSet<HttpStatusCode> PermanentStatuses =
    [
        HttpStatusCode.BadRequest,
        HttpStatusCode.Unauthorized,
        HttpStatusCode.Forbidden,
        HttpStatusCode.NotFound,
        HttpStatusCode.UnprocessableEntity,
    ];

    public static AiProviderException Map(HttpStatusCode status, Exception exception) =>
        new(KindFor(status), Describe(status), exception);

    public static AiProviderException MapUnreachable(Exception exception) =>
        new(AiFailureKind.Transient, "The Groq service could not be reached.", exception);

    public static AiProviderException MapTimeout(Exception exception) =>
        new(AiFailureKind.Transient, "The Groq request timed out.", exception);

    public static AiProviderException MapMalformed(Exception exception) =>
        new(AiFailureKind.Transient, "Groq returned a response that could not be read.", exception);

    private static AiFailureKind KindFor(HttpStatusCode status)
    {
        if (status == HttpStatusCode.TooManyRequests)
        {
            return AiFailureKind.QuotaExceeded;
        }

        if (PermanentStatuses.Contains(status))
        {
            return AiFailureKind.Permanent;
        }

        return AiFailureKind.Transient;
    }

    private static string Describe(HttpStatusCode status) => status switch
    {
        HttpStatusCode.BadRequest => "Groq rejected the request.",
        HttpStatusCode.Unauthorized => "Groq rejected the API key.",
        HttpStatusCode.Forbidden => "Groq denied access for this API key.",
        HttpStatusCode.NotFound => "Groq could not find the configured model.",
        HttpStatusCode.TooManyRequests => "Groq's rate limit or quota was exceeded.",
        _ => $"Groq returned status {(int)status}.",
    };
}
