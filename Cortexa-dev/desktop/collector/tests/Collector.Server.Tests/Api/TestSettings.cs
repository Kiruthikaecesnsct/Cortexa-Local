namespace Collector.Server.Tests.Api;

internal static class TestSettings
{
    public const string Issuer = "cortexa-identity-dev";
    public const string Audience = "cortexa-dev";
    public const string SigningKey = "test-signing-key-with-at-least-64-bytes-so-that-hs512-tokens-can-be-signed";

    public static Dictionary<string, string?> Create(IReadOnlyDictionary<string, string?>? overrides = null)
    {
        var settings = new Dictionary<string, string?>
        {
            ["Cosmos:Endpoint"] = "https://localhost:8081",
            ["Cosmos:Key"] = "dGVzdC1rZXk=",
            ["Messaging:RabbitMq:HostName"] = "localhost",
            ["Messaging:RabbitMq:Port"] = "5672",
            ["Messaging:RabbitMq:VirtualHost"] = "/",
            ["Messaging:RabbitMq:UserName"] = "collector",
            ["Messaging:RabbitMq:Password"] = "test-password",
            ["Identity:Issuer"] = Issuer,
            ["Identity:Audience"] = Audience,
            ["Identity:SigningKey"] = SigningKey,
            ["Identity:InternalKey"] = "test-internal-key"
        };

        foreach (var (key, value) in overrides ?? new Dictionary<string, string?>())
        {
            settings[key] = value;
        }

        return settings;
    }
}
