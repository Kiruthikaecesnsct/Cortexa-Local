using System.Text.Json;
using Microsoft.Azure.Cosmos;

namespace Cortexa.JobOrchestrator.Infrastructure.Persistence;

public sealed class SystemTextJsonCosmosSerializer : CosmosSerializer
{
    private readonly JsonSerializerOptions _options;

    public SystemTextJsonCosmosSerializer(JsonSerializerOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public override T FromStream<T>(Stream stream)
    {
        if (stream is null)
            throw new ArgumentNullException(nameof(stream));

        if (typeof(Stream).IsAssignableFrom(typeof(T)))
            return (T)(object)stream;

        using (stream)
        {
            if (stream.CanSeek && stream.Length == 0)
                return default!;

            return JsonSerializer.Deserialize<T>(stream, _options)!;
        }
    }

    public override Stream ToStream<T>(T input)
    {
        if (input is null)
            throw new ArgumentNullException(nameof(input));

        var memoryStream = new MemoryStream();
        JsonSerializer.Serialize(memoryStream, input, _options);
        memoryStream.Position = 0;
        return memoryStream;
    }
}
