using Collector.Server.Application.Ports;

namespace Collector.Server.Tests.Fakes;

internal sealed class FakeUserStatusReader : IUserStatusReader
{
    public UserStatus? Status { get; set; } = new(true, TestIdentity.Stamp);

    public int CallCount { get; private set; }

    public Task<UserStatus?> GetStatusAsync(string userId, CancellationToken cancellationToken)
    {
        CallCount++;
        return Task.FromResult(Status);
    }
}
