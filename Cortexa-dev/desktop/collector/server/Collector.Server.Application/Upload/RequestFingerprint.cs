using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Collector.Domain.Serialization;
using Collector.Domain.Upload;

namespace Collector.Server.Application.Upload;

public static class RequestFingerprint
{
    public static string Compute(KnowledgeUploadRequest request)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(request, CollectorJson.Options);
        return Convert.ToHexStringLower(SHA256.HashData(payload));
    }

    public static bool Matches(string? stored, string computed) =>
        string.IsNullOrEmpty(stored) || string.Equals(stored, computed, StringComparison.Ordinal);
}
