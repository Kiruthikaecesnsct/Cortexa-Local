using System.Net.Http.Json;
using System.Text.Json;
using Collector.Application.Ports;
using Collector.Domain.Serialization;
using Collector.Infrastructure.Upload.Wire;

namespace Collector.Infrastructure.Upload;

internal static class UploadErrorReader
{
    private const int MaxDetails = 3;

    public static async Task<KnowledgeUploadException> ReadAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        var body = await TryReadBodyAsync(response, cancellationToken);
        var status = (int)response.StatusCode;
        return new KnowledgeUploadException(status, body?.Error, BuildMessage(status, body));
    }

    private static async Task<UploadErrorBody?> TryReadBodyAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        try
        {
            return await response.Content.ReadFromJsonAsync<UploadErrorBody>(CollectorJson.Options, cancellationToken);
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException or InvalidOperationException)
        {
            return null;
        }
    }

    private static string BuildMessage(int status, UploadErrorBody? body)
    {
        var message = string.IsNullOrWhiteSpace(body?.Message) ? $"The server returned status {status}." : body.Message;
        var details = (body?.Errors ?? [])
            .Where(e => !string.IsNullOrWhiteSpace(e.Message))
            .Take(MaxDetails)
            .Select(e => string.IsNullOrWhiteSpace(e.Field) ? e.Message : $"{e.Field}: {e.Message}")
            .ToList();
        return details.Count == 0 ? message : $"{message} {string.Join("; ", details)}";
    }
}
