namespace Collector.Application.Settings;

public sealed record RemoteSourceSettings(string AzureDevOpsOrganization)
{
    public IReadOnlyList<SshConnectionProfile> SshProfiles { get; init; } = Array.Empty<SshConnectionProfile>();
}
