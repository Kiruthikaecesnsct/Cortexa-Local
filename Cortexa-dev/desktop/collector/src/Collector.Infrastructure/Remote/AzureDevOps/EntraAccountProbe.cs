namespace Collector.Infrastructure.Remote.AzureDevOps;

internal static class EntraAccountProbe
{
    public static async Task<bool> AnyAsync(IReadOnlyList<Func<Task<bool>>> probes)
    {
        foreach (var probe in probes)
        {
            if (await probe())
            {
                return true;
            }
        }

        return false;
    }
}
