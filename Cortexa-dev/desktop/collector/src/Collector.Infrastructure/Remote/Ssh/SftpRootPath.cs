namespace Collector.Infrastructure.Remote.Ssh;

public static class SftpRootPath
{
    private const string HomeMarker = "~";
    private const string HomePrefix = "~/";
    private const string CurrentDirectory = ".";

    public static string Resolve(string root)
    {
        if (root == HomeMarker)
        {
            return CurrentDirectory;
        }

        if (!root.StartsWith(HomePrefix, StringComparison.Ordinal))
        {
            return root;
        }

        var relative = root[HomePrefix.Length..].TrimEnd('/');
        return relative.Length == 0 ? CurrentDirectory : relative;
    }
}
