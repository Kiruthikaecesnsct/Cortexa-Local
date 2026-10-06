namespace Collector.Server.Application.Ports;

public sealed record UserStatus(bool Enabled, Guid SecurityStamp);

public interface IUserStatusReader
{
    Task<UserStatus?> GetStatusAsync(string userId, CancellationToken cancellationToken);
}
