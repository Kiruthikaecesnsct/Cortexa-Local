using Collector.Application.Remote.Auth;

namespace Collector.Application.Ports;

public interface IDeviceAuthorizationClient
{
    Task<DeviceCodeGrant> RequestCodeAsync(RemoteAuthMethod method, CancellationToken cancellationToken);

    Task<DevicePollResult> PollAsync(RemoteAuthMethod method, string deviceCode, CancellationToken cancellationToken);

    Task<RefreshResult> RefreshAsync(RemoteAuthMethod method, string refreshToken, CancellationToken cancellationToken);
}
