using System.Text;
using Collector.Application.Ports;
using Collector.Application.Secrets;
using Collector.Infrastructure.Options;
using Collector.Infrastructure.Secrets;
using Collector.Tests.Support;
using Microsoft.Extensions.Logging.Abstractions;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace Collector.Tests.Secrets;

public sealed class EntraTokenCacheStoreTests
{
    private const int SmallCacheBytes = 100;
    private const int MultiPartCacheBytes = 5000;
    private const int MaxStoredBytes = 7000;
    private const int OverBudgetBytes = 20000;

    private static readonly SecretSlot[] PartSlots =
    [
        SecretSlot.EntraIdCachePart1,
        SecretSlot.EntraIdCachePart2,
        SecretSlot.EntraIdCachePart3,
        SecretSlot.EntraIdCachePart4,
        SecretSlot.EntraIdCachePart5,
        SecretSlot.EntraIdCachePart6,
        SecretSlot.EntraIdCachePart7,
        SecretSlot.EntraIdCachePart8,
    ];

    private readonly SizeLimitedStore _secrets = new();

    private EntraTokenCacheStore Store(ISecretStore? secrets = null) =>
        new(secrets ?? _secrets, NullLogger<EntraTokenCacheStore>.Instance);

    private static byte[] Bytes(int length)
    {
        var bytes = new byte[length];
        new Random(length).NextBytes(bytes);
        return bytes;
    }

    private static int EntraSlotCount(SizeLimitedStore store) =>
        store.Values.Keys.Count(slot => slot == SecretSlot.EntraIdCacheIndex || PartSlots.Contains(slot));

    [Fact]
    public async Task SaveAsync_LargerThanOneBlob_RoundTripsAcrossParts()
    {
        var cache = Bytes(MultiPartCacheBytes);

        await Store().SaveAsync(cache, TestSupport.Ct);

        Assert.Equal(cache, await Store().LoadAsync(TestSupport.Ct));
        Assert.Equal("v1:3", _secrets.Values[SecretSlot.EntraIdCacheIndex]);
        Assert.Equal(4, EntraSlotCount(_secrets));
    }

    [Fact]
    public async Task SaveAsync_Shrinking_DeletesTheStaleTail()
    {
        await Store().SaveAsync(Bytes(MultiPartCacheBytes), TestSupport.Ct);
        var smaller = Bytes(SmallCacheBytes);

        await Store().SaveAsync(smaller, TestSupport.Ct);

        Assert.Equal(smaller, await Store().LoadAsync(TestSupport.Ct));
        Assert.Equal(2, EntraSlotCount(_secrets));
        Assert.False(_secrets.Values.ContainsKey(SecretSlot.EntraIdCachePart2));
    }

    [Fact]
    public async Task SaveAsync_Empty_ClearsEverything()
    {
        await Store().SaveAsync(Bytes(MultiPartCacheBytes), TestSupport.Ct);

        await Store().SaveAsync([], TestSupport.Ct);

        Assert.Equal(0, EntraSlotCount(_secrets));
    }

    [Fact]
    public async Task SaveAsync_AtTheEightPartLimit_Stores()
    {
        var cache = Bytes(MaxStoredBytes);

        await Store().SaveAsync(cache, TestSupport.Ct);

        Assert.Equal(cache, await Store().LoadAsync(TestSupport.Ct));
    }

    [Fact]
    public async Task SaveAsync_OverBudget_DoesNotStoreAndDoesNotThrow()
    {
        await Store().SaveAsync(Bytes(OverBudgetBytes), TestSupport.Ct);

        Assert.Equal(0, EntraSlotCount(_secrets));
    }

    [Fact]
    public async Task LoadAsync_MissingPart_ClearsAllAndReturnsEmpty()
    {
        await Store().SaveAsync(Bytes(MultiPartCacheBytes), TestSupport.Ct);
        _secrets.Values.Remove(SecretSlot.EntraIdCachePart2);

        var loaded = await Store().LoadAsync(TestSupport.Ct);

        Assert.Empty(loaded);
        Assert.Equal(0, EntraSlotCount(_secrets));
    }

    [Fact]
    public async Task LoadAsync_CorruptPart_ClearsAllAndReturnsEmpty()
    {
        await Store().SaveAsync(Bytes(MultiPartCacheBytes), TestSupport.Ct);
        _secrets.Values[SecretSlot.EntraIdCachePart1] = "!!not base64!!";

        var loaded = await Store().LoadAsync(TestSupport.Ct);

        Assert.Empty(loaded);
        Assert.Equal(0, EntraSlotCount(_secrets));
    }

    [Theory]
    [InlineData("v2:1")]
    [InlineData("v1:0")]
    [InlineData("v1:9")]
    [InlineData("v1:x")]
    [InlineData("garbage")]
    public async Task LoadAsync_BadIndex_ReturnsEmpty(string index)
    {
        _secrets.Values[SecretSlot.EntraIdCacheIndex] = index;

        Assert.Empty(await Store().LoadAsync(TestSupport.Ct));
    }

    [Fact]
    public async Task LoadAsync_NothingStored_ReturnsEmpty()
    {
        Assert.Empty(await Store().LoadAsync(TestSupport.Ct));
    }

    [Fact]
    public async Task ClearAsync_RemovesIndexAndEveryPart()
    {
        await Store().SaveAsync(Bytes(MultiPartCacheBytes), TestSupport.Ct);
        _secrets.Values[SecretSlot.EntraIdCachePart8] = "leftover";

        await Store().ClearAsync(TestSupport.Ct);

        Assert.Equal(0, EntraSlotCount(_secrets));
    }

    [Fact]
    public async Task ClearAsync_OneDeleteFails_TriesTheRestAndReportsTheFailure()
    {
        var failing = new SizeLimitedStore { FailDeleteFor = SecretSlot.EntraIdCacheIndex };
        await Store(failing).SaveAsync(Bytes(MultiPartCacheBytes), TestSupport.Ct);

        await Assert.ThrowsAsync<SecretStoreException>(() => Store(failing).ClearAsync(TestSupport.Ct));

        Assert.Equal([SecretSlot.EntraIdCacheIndex], failing.Values.Keys);
    }

    [Fact]
    public async Task RoundTrip_ThroughCredentialManager_Works()
    {
        var real = new CredentialManagerStore(
            MsOptions.Create(new SecretsOptions { TargetPrefix = $"Cortexa.Collector.Tests.{Guid.NewGuid():N}" }));
        var store = Store(real);
        var cache = Bytes(MultiPartCacheBytes);
        try
        {
            await store.SaveAsync(cache, TestSupport.Ct);

            Assert.Equal(cache, await store.LoadAsync(TestSupport.Ct));
        }
        finally
        {
            await store.ClearAsync(CancellationToken.None);
        }
    }

    private sealed class SizeLimitedStore : ISecretStore
    {
        public Dictionary<SecretSlot, string> Values { get; } = [];

        public SecretSlot? FailDeleteFor { get; set; }

        public Task<string?> ReadAsync(SecretSlot slot, CancellationToken cancellationToken) =>
            Task.FromResult(Values.TryGetValue(slot, out var value) ? value : null);

        public Task WriteAsync(SecretSlot slot, string value, CancellationToken cancellationToken)
        {
            if (Encoding.UTF8.GetByteCount(value) > CredentialManagerStore.MaxBlobBytes)
            {
                throw new SecretStoreException("too large");
            }

            Values[slot] = value;
            return Task.CompletedTask;
        }

        public Task DeleteAsync(SecretSlot slot, CancellationToken cancellationToken)
        {
            if (slot == FailDeleteFor)
            {
                throw new SecretStoreException("store unavailable");
            }

            Values.Remove(slot);
            return Task.CompletedTask;
        }
    }
}
