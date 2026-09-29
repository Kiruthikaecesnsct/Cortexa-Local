using System.Text;
using RabbitMQ.Client;

namespace Cortexa.JobOrchestrator.Infrastructure.Messaging.RabbitMq;

public static class RabbitMqHeaders
{
    // AMQP delivers string header values as UTF-8 byte arrays.
    public static string? GetString(IDictionary<string, object?>? headers, string key)
    {
        if (headers is null || !headers.TryGetValue(key, out var raw))
            return null;

        return raw switch
        {
            byte[] bytes => Encoding.UTF8.GetString(bytes),
            null => null,
            _ => raw.ToString()
        };
    }

    public static int GetInt(IDictionary<string, object?>? headers, string key)
    {
        if (headers is null || !headers.TryGetValue(key, out var raw))
            return 0;

        return raw switch
        {
            int i => i,
            long l => (int)l,
            byte[] bytes when int.TryParse(Encoding.UTF8.GetString(bytes), out var parsed) => parsed,
            _ => 0
        };
    }

    public static string? ResolveBatchId(IReadOnlyBasicProperties properties)
    {
        var batchId = GetString(properties.Headers, SessionKeyResolver.BatchIdProperty);
        if (!string.IsNullOrWhiteSpace(batchId))
            return batchId;

        var sessionId = GetString(properties.Headers, RabbitMqNames.SessionIdHeader);
        return string.IsNullOrWhiteSpace(sessionId) ? null : SessionKeyResolver.BatchIdFromSessionId(sessionId);
    }

    public static Dictionary<string, object?> Copy(IDictionary<string, object?>? headers) =>
        headers is null ? [] : new Dictionary<string, object?>(headers);
}
