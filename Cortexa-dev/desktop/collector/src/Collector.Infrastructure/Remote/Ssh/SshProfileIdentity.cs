using Collector.Application.Settings;

namespace Collector.Infrastructure.Remote.Ssh;

public static class SshProfileIdentity
{
    public static bool IsSameConnection(SshConnectionProfile? left, SshConnectionProfile right) =>
        left is not null
        && string.Equals(left.Host, right.Host, StringComparison.OrdinalIgnoreCase)
        && left.Port == right.Port
        && string.Equals(left.Username, right.Username, StringComparison.Ordinal)
        && string.Equals(left.KeyFilePath, right.KeyFilePath, StringComparison.Ordinal)
        && string.Equals(left.RemoteRoot, right.RemoteRoot, StringComparison.Ordinal);
}
