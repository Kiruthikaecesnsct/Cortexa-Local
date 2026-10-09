using Collector.Domain.Remote;

namespace Collector.Presentation.ViewModels;

public static class BranchOrdering
{
    private const int ShortShaLength = 7;

    public static IReadOnlyList<RemoteBranch> Order(IEnumerable<RemoteBranch> branches, string defaultBranch, string search)
    {
        var needle = search.Trim();
        return
        [
            .. branches
                .Where(branch => needle.Length == 0 || branch.Name.Contains(needle, StringComparison.OrdinalIgnoreCase))
                .OrderBy(branch => branch.Name == defaultBranch ? 0 : 1)
                .ThenBy(branch => branch.Name, StringComparer.OrdinalIgnoreCase),
        ];
    }

    public static string? ShortSha(string? commitSha) =>
        string.IsNullOrEmpty(commitSha) ? null : commitSha[..Math.Min(ShortShaLength, commitSha.Length)];
}
