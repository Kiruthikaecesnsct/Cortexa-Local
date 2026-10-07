using Collector.Application.Secrets;
using Collector.Infrastructure.Options;
using Collector.Infrastructure.Secrets;
using Collector.Tests.Support;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace Collector.Tests.Secrets;

public sealed class CredentialManagerStoreTests : IAsyncLifetime
{
    private readonly CredentialManagerStore _store =
        new(MsOptions.Create(new SecretsOptions { TargetPrefix = $"Cortexa.Collector.Tests.{Guid.NewGuid():N}" }));

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public async ValueTask DisposeAsync()
    {
        foreach (var slot in Enum.GetValues<SecretSlot>())
        {
            await _store.DeleteAsync(slot, CancellationToken.None);
        }
    }

    [Fact]
    public async Task Round_trips_a_value()
    {
        await _store.WriteAsync(SecretSlot.AnthropicApiKey, "sk-test-value", TestSupport.Ct);

        Assert.Equal("sk-test-value", await _store.ReadAsync(SecretSlot.AnthropicApiKey, TestSupport.Ct));
    }

    [Fact]
    public async Task Overwrites_an_existing_value()
    {
        await _store.WriteAsync(SecretSlot.GeminiApiKey, "first", TestSupport.Ct);
        await _store.WriteAsync(SecretSlot.GeminiApiKey, "second", TestSupport.Ct);

        Assert.Equal("second", await _store.ReadAsync(SecretSlot.GeminiApiKey, TestSupport.Ct));
    }

    [Fact]
    public async Task Missing_slot_reads_as_null()
    {
        Assert.Null(await _store.ReadAsync(SecretSlot.CortexaAccessToken, TestSupport.Ct));
    }

    [Fact]
    public async Task Delete_removes_the_value_and_missing_delete_is_quiet()
    {
        await _store.WriteAsync(SecretSlot.CortexaRefreshToken, "token", TestSupport.Ct);

        await _store.DeleteAsync(SecretSlot.CortexaRefreshToken, TestSupport.Ct);
        await _store.DeleteAsync(SecretSlot.CortexaRefreshToken, TestSupport.Ct);

        Assert.Null(await _store.ReadAsync(SecretSlot.CortexaRefreshToken, TestSupport.Ct));
    }

    [Fact]
    public async Task Slots_are_independent()
    {
        await _store.WriteAsync(SecretSlot.CortexaAccessToken, "access", TestSupport.Ct);
        await _store.WriteAsync(SecretSlot.CortexaRefreshToken, "refresh", TestSupport.Ct);

        Assert.Equal("access", await _store.ReadAsync(SecretSlot.CortexaAccessToken, TestSupport.Ct));
        Assert.Equal("refresh", await _store.ReadAsync(SecretSlot.CortexaRefreshToken, TestSupport.Ct));
    }

    [Fact]
    public async Task Stores_a_value_exactly_at_the_byte_cap()
    {
        var value = new string('a', CredentialManagerStore.MaxBlobBytes);

        await _store.WriteAsync(SecretSlot.CortexaAccessToken, value, TestSupport.Ct);

        Assert.Equal(value, await _store.ReadAsync(SecretSlot.CortexaAccessToken, TestSupport.Ct));
    }

    [Fact]
    public async Task Rejects_a_value_over_the_byte_cap_without_writing()
    {
        var value = new string('a', CredentialManagerStore.MaxBlobBytes + 1);

        await Assert.ThrowsAsync<SecretStoreException>(
            () => _store.WriteAsync(SecretSlot.CortexaAccessToken, value, TestSupport.Ct));

        Assert.Null(await _store.ReadAsync(SecretSlot.CortexaAccessToken, TestSupport.Ct));
    }

    [Fact]
    public async Task Counts_bytes_not_characters_for_the_cap()
    {
        var value = new string('é', (CredentialManagerStore.MaxBlobBytes / 2) + 1);

        await Assert.ThrowsAsync<SecretStoreException>(
            () => _store.WriteAsync(SecretSlot.CortexaAccessToken, value, TestSupport.Ct));
    }

    [Fact]
    public async Task Round_trips_non_ascii_text()
    {
        const string Value = "kéy-日本";

        await _store.WriteAsync(SecretSlot.AnthropicApiKey, Value, TestSupport.Ct);

        Assert.Equal(Value, await _store.ReadAsync(SecretSlot.AnthropicApiKey, TestSupport.Ct));
    }
}
