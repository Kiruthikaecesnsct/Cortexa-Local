using Collector.Application.Secrets;
using Collector.Infrastructure.Secrets;
using Collector.Tests.Support;

namespace Collector.Tests.Secrets;

public sealed class GeminiKeyStoreTests
{
    private const string LegacyRawKey = "legacy-raw-gemini-key";

    private static GeminiKeyStore Store(InMemorySecretStore secrets) => new(secrets);

    [Fact]
    public async Task GetKeysAsync_LegacyRawKeyStored_MigratesToASingleElementList()
    {
        var secrets = new InMemorySecretStore();
        secrets.Values[SecretSlot.GeminiApiKey] = LegacyRawKey;
        var store = Store(secrets);

        var list = await store.GetKeysAsync(TestSupport.Ct);

        var migrated = Assert.Single(list.Keys);
        Assert.Equal(LegacyRawKey, migrated.Key);
        Assert.False(string.IsNullOrWhiteSpace(migrated.Id));
    }

    [Fact]
    public async Task GetKeysAsync_LegacyRawKeyStored_PersistsTheMigratedListNotJustReturnsItOnce()
    {
        var secrets = new InMemorySecretStore();
        secrets.Values[SecretSlot.GeminiApiKey] = LegacyRawKey;
        var store = Store(secrets);

        var first = await store.GetKeysAsync(TestSupport.Ct);
        var persistedRaw = secrets.Values[SecretSlot.GeminiApiKey];
        var second = await store.GetKeysAsync(TestSupport.Ct);

        Assert.Contains(LegacyRawKey, persistedRaw, StringComparison.Ordinal);
        Assert.StartsWith("[", persistedRaw.TrimStart(), StringComparison.Ordinal);
        Assert.Equal(first.Keys.Single().Id, second.Keys.Single().Id);
    }

    [Fact]
    public async Task AddKeyAsync_PackedListExceedsTheCredentialByteBudget_ThrowsSecretStoreException()
    {
        var secrets = new InMemorySecretStore();
        var store = Store(secrets);
        var nearMaxLengthKey = new string('k', 505);

        for (var index = 0; index < 4; index++)
        {
            await store.AddKeyAsync(nearMaxLengthKey + index, TestSupport.Ct);
        }

        await Assert.ThrowsAsync<SecretStoreException>(
            () => store.AddKeyAsync(nearMaxLengthKey + "4", TestSupport.Ct));
    }

    [Fact]
    public async Task ReorderAsync_NewOrder_IsReflectedOnTheNextRead()
    {
        var secrets = new InMemorySecretStore();
        var store = Store(secrets);
        var first = await store.AddKeyAsync("first-key-value", TestSupport.Ct);
        var second = await store.AddKeyAsync("second-key-value", TestSupport.Ct);

        await store.ReorderAsync([second.Id, first.Id], TestSupport.Ct);

        var list = await store.GetKeysAsync(TestSupport.Ct);
        Assert.Equal([second.Id, first.Id], list.Keys.Select(key => key.Id));
    }
}
