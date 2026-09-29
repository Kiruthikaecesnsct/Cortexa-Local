using System.Text.Json;
using System.Text.Json.Nodes;

namespace Cortexa.Identity.Application.Auditing;

public static class AuditDetailSerializer
{
    private const string RedactedPlaceholder = "[REDACTED]";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private static readonly string[] BlockedKeyFragments =
    {
        "password",
        "hash",
        "token",
        "secret",
        "apikey",
        "credential",
        "signingkey"
    };

    private static readonly string[] BlockedExactKeys =
    {
        "email",
        "username",
        "phone",
        "phonenumber",
        "ssn",
        "address",
        "idtoken",
        "accesstoken",
        "refreshtoken"
    };

    public static string? Serialize(object? details)
    {
        if (details is null)
            return null;

        var node = JsonSerializer.SerializeToNode(details, SerializerOptions);
        if (node is JsonObject rootObject)
            RedactObject(rootObject);
        else if (node is JsonArray rootArray)
            RedactArray(rootArray);

        return node?.ToJsonString(SerializerOptions);
    }

    private static void RedactObject(JsonObject jsonObject)
    {
        foreach (var key in jsonObject.Select(kvp => kvp.Key).ToList())
        {
            if (IsBlockedKey(key))
            {
                jsonObject[key] = RedactedPlaceholder;
                continue;
            }

            RedactValue(jsonObject[key]);
        }
    }

    private static void RedactArray(JsonArray jsonArray)
    {
        foreach (var item in jsonArray)
            RedactValue(item);
    }

    private static void RedactValue(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject childObject:
                RedactObject(childObject);
                break;
            case JsonArray childArray:
                RedactArray(childArray);
                break;
        }
    }

    private static bool IsBlockedKey(string key)
    {
        var normalized = key.Replace("_", string.Empty).ToLowerInvariant();

        if (Array.Exists(BlockedExactKeys, blocked => blocked == normalized))
            return true;

        return Array.Exists(BlockedKeyFragments, fragment => normalized.Contains(fragment));
    }
}
