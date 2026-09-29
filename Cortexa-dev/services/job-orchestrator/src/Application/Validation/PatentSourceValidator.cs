using Cortexa.JobOrchestrator.Domain.Entities;

namespace Cortexa.JobOrchestrator.Application.Validation;

// Stateless validation for patent source names and credential shape. No I/O and
// no dependencies, so this stays a static helper (mirrors
// ModelConfigValidator.ValidateSeedingMode) rather than a DI service.
public static class PatentSourceValidator
{
    private const int MinKeyLength = 8;
    private const int MaxKeyLength = 512;

    public static bool TryParseSource(string? raw, out PatentSource source)
    {
        source = default;
        return !string.IsNullOrWhiteSpace(raw) && Enum.TryParse(raw, ignoreCase: true, out source);
    }

    public static string? ValidateSecretWrite(PatentSource source, string? apiKey, string? consumerKey, string? oauthSecret)
    {
        return source == PatentSource.Epo
            ? ValidateEpoPair(consumerKey, oauthSecret)
            : ValidateSingleKey(apiKey);
    }

    private static string? ValidateSingleKey(string? key)
    {
        return string.IsNullOrWhiteSpace(key)
            ? "api_key is required."
            : ValidateKeyShape(key, "api_key");
    }

    private static string? ValidateEpoPair(string? consumerKey, string? oauthSecret)
    {
        var hasConsumer = !string.IsNullOrWhiteSpace(consumerKey);
        var hasOauth = !string.IsNullOrWhiteSpace(oauthSecret);

        if (!hasConsumer && !hasOauth)
            return "consumer_key and oauth_secret are required.";

        if (hasConsumer != hasOauth)
            return "consumer_key and oauth_secret must be provided together.";

        return ValidateKeyShape(consumerKey!, "consumer_key") ?? ValidateKeyShape(oauthSecret!, "oauth_secret");
    }

    private static string? ValidateKeyShape(string key, string fieldName)
    {
        var trimmed = key.Trim();

        if (trimmed.Length < MinKeyLength || trimmed.Length > MaxKeyLength)
            return $"{fieldName} must be between {MinKeyLength} and {MaxKeyLength} characters.";

        if (trimmed.Any(char.IsWhiteSpace))
            return $"{fieldName} must not contain whitespace.";

        return null;
    }
}
