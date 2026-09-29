using Cortexa.ApiGateway.Api.Auth;

namespace Cortexa.ApiGateway.Tests;

public sealed class FakeUserStatusClient : IUserStatusClient
{
    public bool Enabled { get; set; } = true;

    public Guid SecurityStamp { get; set; } = TestJwtFactory.DefaultSecurityStamp;

    public bool ShouldFail { get; set; }

    public int CallCount { get; private set; }

    public Task<UserStatusResult?> GetStatusAsync(string userId, CancellationToken cancellationToken)
    {
        CallCount++;

        if (ShouldFail)
        {
            return Task.FromResult<UserStatusResult?>(null);
        }

        return Task.FromResult<UserStatusResult?>(new UserStatusResult(Enabled, SecurityStamp));
    }
}
