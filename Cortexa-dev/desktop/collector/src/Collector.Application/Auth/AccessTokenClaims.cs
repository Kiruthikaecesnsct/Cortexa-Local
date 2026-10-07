using System.Buffers.Text;
using System.Text;
using System.Text.Json;

namespace Collector.Application.Auth;

public static class AccessTokenClaims
{
    private const int JwtPartCount = 3;
    private const int PayloadIndex = 1;

    public static string? ReadEmail(string accessToken) =>
        ReadPayload(accessToken, root => ReadString(root, "email"));

    public static DateTimeOffset? ReadExpiry(string accessToken) =>
        ReadPayload(accessToken, ReadExp);

    private static T? ReadPayload<T>(string accessToken, Func<JsonElement, T?> read)
    {
        var parts = accessToken.Split('.');
        if (parts.Length != JwtPartCount || !Base64Url.IsValid(parts[PayloadIndex]))
        {
            return default;
        }

        try
        {
            using var document = JsonDocument.Parse(Base64Url.DecodeFromChars(parts[PayloadIndex]));
            return document.RootElement.ValueKind == JsonValueKind.Object ? read(document.RootElement) : default;
        }
        catch (JsonException)
        {
            return default;
        }
    }

    private static string? ReadString(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static DateTimeOffset? ReadExp(JsonElement root) =>
        root.TryGetProperty("exp", out var value) && value.TryGetInt64(out var seconds)
            ? DateTimeOffset.FromUnixTimeSeconds(seconds)
            : null;
}
