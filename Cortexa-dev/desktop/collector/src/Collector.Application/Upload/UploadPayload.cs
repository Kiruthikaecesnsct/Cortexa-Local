using System.Security.Cryptography;
using System.Text.Json;
using Collector.Domain.Serialization;
using Collector.Domain.Upload;

namespace Collector.Application.Upload;

public sealed record UploadPayload(byte[] Body, string Sha256)
{
    public static UploadPayload From(KnowledgeUploadRequest request)
    {
        var body = JsonSerializer.SerializeToUtf8Bytes(request, CollectorJson.Options);
        return new UploadPayload(body, Convert.ToHexStringLower(SHA256.HashData(body)));
    }

    public int CountItems()
    {
        try
        {
            var request = JsonSerializer.Deserialize<KnowledgeUploadRequest>(Body, CollectorJson.Options);
            return request?.Documents.Sum(document => document.KnowledgeItems.Count) ?? 0;
        }
        catch (JsonException)
        {
            return 0;
        }
    }
}
