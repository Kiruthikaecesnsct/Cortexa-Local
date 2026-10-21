using Collector.Application.Ports;
using Collector.Application.Remote.Auth;
using Collector.Domain.Enums;
using Collector.Infrastructure.Remote.AzureDevOps;

namespace Collector.Tests.Remote;

public sealed class EntraAccountProbeTests
{
    [Fact]
    public async Task AnyAsync_OnlyTheBrokerProbeSucceeds_ReturnsTrue()
    {
        var calls = new List<string>();

        var found = await EntraAccountProbe.AnyAsync(
        [
            () => Probe(calls, "plain", false),
            () => Probe(calls, "broker", false),
            () => Probe(calls, "os", true),
        ]);

        Assert.True(found);
        Assert.Equal(["plain", "broker", "os"], calls);
    }

    [Fact]
    public async Task AnyAsync_FirstProbeSucceeds_SkipsTheRest()
    {
        var calls = new List<string>();

        var found = await EntraAccountProbe.AnyAsync([() => Probe(calls, "plain", true), () => Probe(calls, "os", true)]);

        Assert.True(found);
        Assert.Equal(["plain"], calls);
    }

    [Fact]
    public async Task AnyAsync_NoProbeSucceeds_ReturnsFalse()
    {
        Assert.False(await EntraAccountProbe.AnyAsync([() => Task.FromResult(false)]));
    }

    [Fact]
    public async Task RestoreAsync_BrokerOnlySignIn_SetsEntraId()
    {
        var state = new RemoteAuthState();
        var calls = new List<string>();
        var client = new ProbeBackedClient(() => EntraAccountProbe.AnyAsync(
        [
            () => Probe(calls, "plain", false),
            () => Probe(calls, "os", true),
        ]));

        await new EntraIdSignInService(client, state).RestoreAsync(TestContext.Current.CancellationToken);

        Assert.Equal(RemoteAuthMethod.EntraId, state.MethodFor(SourceType.AzureDevops));
    }

    private static Task<bool> Probe(List<string> calls, string name, bool result)
    {
        calls.Add(name);
        return Task.FromResult(result);
    }

    private sealed class ProbeBackedClient(Func<Task<bool>> has) : IEntraIdAuthClient
    {
        public Task<bool> HasAccountAsync(CancellationToken cancellationToken) => has();

        public Task<EntraSignInOutcome> AcquireSilentAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<EntraSignInOutcome> AcquireInteractiveAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<EntraSignInOutcome> AcquireWithDeviceCodeAsync(Func<EntraDeviceCode, Task> onCode, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task SignOutAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
