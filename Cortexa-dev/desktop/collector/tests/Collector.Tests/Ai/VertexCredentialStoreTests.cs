using Collector.Application.Secrets;
using Collector.Infrastructure.Secrets;
using Collector.Tests.Support;

namespace Collector.Tests.Ai;

public sealed class VertexCredentialStoreTests
{
    private const string ServiceAccountJson = "{\"type\":\"service_account\",\"client_email\":\"collector@cortexa.iam.gserviceaccount.com\"}";

    [Fact]
    public async Task SaveAndGet_RoundTripsThroughSecretStore()
    {
        var secrets = new InMemorySecretStore();
        var store = new VertexCredentialStore(secrets);

        await store.SaveServiceAccountJsonAsync(ServiceAccountJson, TestSupport.Ct);
        var loaded = await store.GetServiceAccountJsonAsync(TestSupport.Ct);

        Assert.Equal(ServiceAccountJson, loaded);
        Assert.Equal(ServiceAccountJson, secrets.Values[SecretSlot.VertexServiceAccount]);
    }

    [Fact]
    public async Task Get_NothingStored_ReturnsNull()
    {
        var store = new VertexCredentialStore(new InMemorySecretStore());

        var loaded = await store.GetServiceAccountJsonAsync(TestSupport.Ct);

        Assert.Null(loaded);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not json")]
    [InlineData("[1,2,3]")]
    public async Task Save_InvalidJson_ThrowsWithoutStoringAnything(string invalid)
    {
        var secrets = new InMemorySecretStore();
        var store = new VertexCredentialStore(secrets);

        await Assert.ThrowsAsync<SecretStoreException>(() => store.SaveServiceAccountJsonAsync(invalid, TestSupport.Ct));

        Assert.False(secrets.Values.ContainsKey(SecretSlot.VertexServiceAccount));
    }
}
