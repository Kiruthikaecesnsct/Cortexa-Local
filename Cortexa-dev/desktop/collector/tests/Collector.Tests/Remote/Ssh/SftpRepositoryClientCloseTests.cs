using Collector.Application.Settings;
using Collector.Infrastructure.Remote.Ssh;
using Collector.Infrastructure.Secrets;
using Collector.Tests.Support;
using Microsoft.Extensions.Logging.Abstractions;

namespace Collector.Tests.Remote.Ssh;

public sealed class SftpRepositoryClientCloseTests
{
    private static SftpRepositoryClient CreateClient() => new(
        new SftpConnectionFactory(new InMemorySessionCredentials()),
        new SettingsService(new FakeUserSettingsStore(), new InMemorySecretStore()),
        RemoteData.Options(),
        NullLogger<SftpRepositoryClient>.Instance);

    [Fact]
    public async Task CloseAsync_NothingConnected_Completes()
    {
        using var client = CreateClient();

        await client.CloseAsync(TestSupport.Ct);
    }

    [Fact]
    public async Task CloseAsync_AfterDispose_IsANoOp()
    {
        var client = CreateClient();
        client.Dispose();

        await client.CloseAsync(TestSupport.Ct);
    }

    [Fact]
    public void Dispose_Twice_DoesNotThrow()
    {
        var client = CreateClient();

        client.Dispose();
        client.Dispose();
    }
}
