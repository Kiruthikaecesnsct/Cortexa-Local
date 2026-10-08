namespace Collector.Infrastructure.Options;

public sealed class GatewayOptions
{
    public const string SectionName = "Gateway";

    public string BaseUrl { get; set; } = string.Empty;
}

public sealed class CollectorServerOptions
{
    public const string SectionName = "CollectorServer";

    public string BaseUrl { get; set; } = string.Empty;

    public int ReadTimeoutSeconds { get; set; } = 15;

    public int[] ReadRetryDelaysMs { get; set; } = [];
}

public sealed class CacheOptions
{
    public const string SectionName = "Cache";

    public string DatabasePath { get; set; } = @"%LOCALAPPDATA%\Cortexa\Collector\cache.db";
}

public sealed class SecretsOptions
{
    public const string SectionName = "Secrets";

    public string TargetPrefix { get; set; } = "Cortexa.Collector";
}

public sealed class UserSettingsOptions
{
    public const string SectionName = "UserSettings";

    public string Path { get; set; } = @"%LOCALAPPDATA%\Cortexa\Collector\usersettings.json";
}

public sealed class HistoryOptions
{
    public const string SectionName = "History";

    public int PollSeconds { get; set; } = 5;

    public string TextEditorPath { get; set; } = "notepad.exe";
}
