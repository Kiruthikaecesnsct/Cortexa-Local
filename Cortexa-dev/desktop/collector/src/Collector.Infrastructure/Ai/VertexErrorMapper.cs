using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Collector.Application.Ports;

namespace Collector.Infrastructure.Ai;

internal static partial class VertexErrorMapper
{
    private static readonly HashSet<HttpStatusCode> PermanentStatuses =
    [
        HttpStatusCode.BadRequest,
        HttpStatusCode.Unauthorized,
        HttpStatusCode.Forbidden,
        HttpStatusCode.NotFound,
        HttpStatusCode.UnprocessableEntity,
    ];

    public static AiProviderException Map(HttpStatusCode status, string? body, string model)
    {
        if (status == HttpStatusCode.BadRequest && TryReadInputTooLarge(body, model) is { } tooLarge)
        {
            return tooLarge;
        }

        return new AiProviderException(KindFor(status), Describe(status), statusCode: (int)status);
    }

    public static AiProviderException MapUnreachable(Exception exception) =>
        new(AiFailureKind.Transient, "The Vertex AI service could not be reached.", exception);

    public static AiProviderException MapTimeout(Exception exception) =>
        new(AiFailureKind.Transient, "The Vertex AI request timed out.", exception);

    public static AiProviderException MapMalformed(Exception exception) =>
        new(AiFailureKind.Transient, "Vertex AI returned a response that could not be read.", exception);

    private static AiInputTooLargeException? TryReadInputTooLarge(string? body, string model)
    {
        var message = ReadErrorMessage(body);
        if (message is null)
        {
            return null;
        }

        var match = InputTooLargePattern().Match(message);
        return match.Success
            ? new AiInputTooLargeException(int.Parse(match.Groups[1].Value), int.Parse(match.Groups[2].Value), model)
            : null;
    }

    private static string? ReadErrorMessage(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        try
        {
            var envelope = JsonSerializer.Deserialize(body, VertexJson.Default.VertexErrorEnvelopeWire);
            return envelope?.Error?.Message;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static AiFailureKind KindFor(HttpStatusCode status)
    {
        if (status == HttpStatusCode.TooManyRequests)
        {
            return AiFailureKind.QuotaExceeded;
        }

        return PermanentStatuses.Contains(status) ? AiFailureKind.Permanent : AiFailureKind.Transient;
    }

    private static string Describe(HttpStatusCode status) => status switch
    {
        HttpStatusCode.BadRequest => "Vertex AI rejected the request.",
        HttpStatusCode.Unauthorized => "Vertex AI rejected the credentials.",
        HttpStatusCode.Forbidden => "Vertex AI denied access for this project.",
        HttpStatusCode.NotFound => "Vertex AI could not find the configured model.",
        HttpStatusCode.TooManyRequests => "Vertex AI's rate limit or quota was exceeded.",
        _ => $"Vertex AI returned status {(int)status}.",
    };

    [GeneratedRegex(
        @"input token count \((\d+)\) exceeds the maximum number of tokens allowed \((\d+)\)",
        RegexOptions.IgnoreCase)]
    private static partial Regex InputTooLargePattern();
}
