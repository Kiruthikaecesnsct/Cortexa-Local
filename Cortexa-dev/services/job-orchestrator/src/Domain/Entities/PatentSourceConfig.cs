namespace Cortexa.JobOrchestrator.Domain.Entities;

public enum PatentSource
{
    Uspto,
    Epo,
    Lens
}

public static class PatentSecretNames
{
    public const string UsptoApiKey = "uspto-api-key";
    public const string EpoConsumerKey = "epo-consumer-key";
    public const string EpoOAuthSecret = "epo-oauth-secret";
    public const string LensApiKey = "lens-api-key";

    public static readonly IReadOnlyCollection<string> All =
    [
        UsptoApiKey,
        EpoConsumerKey,
        EpoOAuthSecret,
        LensApiKey
    ];
}

public static class PatentCredentialStatus
{
    public const string Set = "set";
    public const string NotSet = "not_set";

    public static string From(bool exists) => exists ? Set : NotSet;
}

public sealed record PatentSourceFlag(bool Enabled);

public sealed record PatentSourceConfig(
    PatentSourceFlag Uspto,
    PatentSourceFlag Epo,
    PatentSourceFlag Lens,
    int ConfigVersion,
    DateTimeOffset UpdatedAt,
    string UpdatedBy)
{
    public PatentSourceConfig WithFlag(PatentSource source, bool enabled) => source switch
    {
        PatentSource.Uspto => this with { Uspto = new PatentSourceFlag(enabled) },
        PatentSource.Epo => this with { Epo = new PatentSourceFlag(enabled) },
        PatentSource.Lens => this with { Lens = new PatentSourceFlag(enabled) },
        _ => throw new ArgumentOutOfRangeException(nameof(source))
    };
}
