namespace Collector.Infrastructure.Options;

public class RemoteProviderOptions
{
    public string BaseUrl { get; set; } = string.Empty;

    public string ApiVersion { get; set; } = string.Empty;

    public int MinRemaining { get; set; }
}

public sealed class GitHubSourceOptions : RemoteProviderOptions
{
    public GitHubSourceOptions()
    {
        BaseUrl = "https://api.github.com";
        ApiVersion = "2026-03-10";
        MinRemaining = 50;
    }
}

public sealed class AzureDevOpsSourceOptions : RemoteProviderOptions
{
    public AzureDevOpsSourceOptions()
    {
        BaseUrl = "https://dev.azure.com";
        ApiVersion = "7.1";
        MinRemaining = 10;
    }

    public string Organization { get; set; } = string.Empty;
}

public sealed class RateLimitOptions
{
    public int MaxPauseSeconds { get; set; } = 900;

    public int MaxRetries { get; set; } = 3;

    public int SecondaryWaitSeconds { get; set; } = 60;

    public int MaxConcurrency { get; set; } = 4;
}

public sealed class SshProfileOptions
{
    public string Host { get; set; } = string.Empty;

    public int Port { get; set; } = 22;

    public string Username { get; set; } = string.Empty;

    public string KeyFilePath { get; set; } = string.Empty;

    public string PinnedFingerprint { get; set; } = string.Empty;

    public string RemoteRoot { get; set; } = string.Empty;
}

public sealed class SshSourceOptions
{
    public int DefaultPort { get; set; } = 22;

    public int ConnectTimeoutSeconds { get; set; } = 20;

    public int MaxWalkDepth { get; set; } = 32;

    public bool FollowSymlinks { get; set; }

    public List<SshProfileOptions> Profiles { get; set; } = [];
}

public sealed class RemoteSourceOptions
{
    public const string SectionName = "RemoteSources";

    public int TimeoutSeconds { get; set; } = 20;

    public int MaxPages { get; set; } = 50;

    public string CacheRoot { get; set; } = @"%LOCALAPPDATA%\Cortexa\Collector\remote";

    public GitHubSourceOptions GitHub { get; set; } = new();

    public AzureDevOpsSourceOptions AzureDevOps { get; set; } = new();

    public SshSourceOptions Ssh { get; set; } = new();

    public RateLimitOptions RateLimit { get; set; } = new();
}
