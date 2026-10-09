using Collector.Infrastructure.Options;

namespace Collector.Infrastructure.Remote.Cortexa;

internal static class CortexaCloneRoute
{
    public const string GitHubTag = "github";
    public const string AzureDevOpsTag = "azure-devops";

    private const int MaxOwnerLength = 64;
    private const int MaxRepositoryLength = 129;
    private const int MaxBranchLength = 255;

    public static IReadOnlyList<string> Tags { get; } = [GitHubTag, AzureDevOpsTag];

    public static string? Prefix(CortexaRepoSourceOptions options, string? tag) => tag switch
    {
        GitHubTag => options.GitHubClonesPath.Trim('/'),
        AzureDevOpsTag => options.AzureDevOpsClonesPath.Trim('/'),
        _ => null,
    };

    public static string FilesPath(string prefix, string owner, string repository, string branch) =>
        $"{prefix}/files?{Query(owner, repository, branch)}";

    public static string DownloadPath(string prefix, string owner, string repository, string branch) =>
        $"{prefix}/download?{Query(owner, repository, branch)}";

    public static bool IsQueryable(string owner, string repository, string branch) =>
        IsWithin(owner, MaxOwnerLength) && IsWithin(repository, MaxRepositoryLength) && IsWithin(branch, MaxBranchLength);

    private static bool IsWithin(string value, int maxLength) => value.Length is > 0 && value.Length <= maxLength;

    private static string Query(string owner, string repository, string branch) =>
        $"owner={Uri.EscapeDataString(owner)}&repository={Uri.EscapeDataString(repository)}&branch={Uri.EscapeDataString(branch)}";
}
