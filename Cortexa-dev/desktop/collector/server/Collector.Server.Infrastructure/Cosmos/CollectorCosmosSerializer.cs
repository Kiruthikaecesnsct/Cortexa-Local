using System.Text.Json;
using Microsoft.Azure.Cosmos;

namespace Collector.Server.Infrastructure.Cosmos;

public sealed class CollectorCosmosSerializer(JsonSerializerOptions options) : CosmosSerializer
{
    private readonly JsonSerializerOptions _options = options ?? throw new ArgumentNullException(nameof(options));

    public override T FromStream<T>(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        if (typeof(Stream).IsAssignableFrom(typeof(T)))
        {
            return (T)(object)stream;
        }

        using (stream)
        {
            if (stream.CanSeek && stream.Length == 0)
            {
                return default!;
            }

            return JsonSerializer.Deserialize<T>(stream, _options)!;
        }
    }

    public override Stream ToStream<T>(T input)
    {
        ArgumentNullException.ThrowIfNull(input);

        var stream = new MemoryStream();
        JsonSerializer.Serialize(stream, input, _options);
        stream.Position = 0;
        return stream;
    }
}
