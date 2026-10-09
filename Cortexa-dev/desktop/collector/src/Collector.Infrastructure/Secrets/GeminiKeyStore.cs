using System.Text;
using System.Text.Json;
using Collector.Application.Ports;
using Collector.Application.Secrets;
using Collector.Application.Settings;

namespace Collector.Infrastructure.Secrets;

public sealed class GeminiKeyStore(ISecretStore secrets) : IGeminiKeyStore
{
    private static readonly UTF8Encoding Utf8 = new(false);
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<GeminiKeyList> GetKeysAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            return await LoadLockedAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<GeminiKey> AddKeyAsync(string rawKey, CancellationToken cancellationToken)
    {
        var normalized = NormalizeKey(rawKey);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var current = await LoadLockedAsync(cancellationToken);
            var created = new GeminiKey(NewId(), normalized);
            await SaveLockedAsync(new GeminiKeyList([.. current.Keys, created]), cancellationToken);
            return created;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task RemoveKeyAsync(string id, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var current = await LoadLockedAsync(cancellationToken);
            var remaining = current.Keys.Where(key => !string.Equals(key.Id, id, StringComparison.Ordinal));
            await SaveLockedAsync(new GeminiKeyList([.. remaining]), cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task ReorderAsync(IReadOnlyList<string> orderedIds, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var current = await LoadLockedAsync(cancellationToken);
            var byId = current.Keys.ToDictionary(key => key.Id);
            var reordered = orderedIds.Where(byId.ContainsKey).Select(id => byId[id]);
            await SaveLockedAsync(new GeminiKeyList([.. reordered]), cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<GeminiKeyList> LoadLockedAsync(CancellationToken cancellationToken)
    {
        var raw = await secrets.ReadAsync(SecretSlot.GeminiApiKey, cancellationToken);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return GeminiKeyList.Empty;
        }

        if (TryParse(raw, out var parsed))
        {
            return parsed;
        }

        var migrated = new GeminiKeyList([new GeminiKey(NewId(), raw.Trim())]);
        await SaveLockedAsync(migrated, cancellationToken);
        return migrated;
    }

    private async Task SaveLockedAsync(GeminiKeyList list, CancellationToken cancellationToken)
    {
        if (list.Keys.Count == 0)
        {
            await secrets.DeleteAsync(SecretSlot.GeminiApiKey, cancellationToken);
            return;
        }

        var json = JsonSerializer.Serialize(list.Keys.Select(ToDto).ToArray());
        EnsureBudget(json);
        await secrets.WriteAsync(SecretSlot.GeminiApiKey, json, cancellationToken);
    }

    private static bool TryParse(string raw, out GeminiKeyList list)
    {
        try
        {
            var dto = JsonSerializer.Deserialize<List<GeminiKeyDto>>(raw);
            list = dto is null ? GeminiKeyList.Empty : new GeminiKeyList([.. dto.Select(ToKey)]);
            return dto is not null;
        }
        catch (JsonException)
        {
            list = GeminiKeyList.Empty;
            return false;
        }
    }

    private static void EnsureBudget(string json)
    {
        if (Utf8.GetByteCount(json) > CredentialManagerStore.MaxBlobBytes)
        {
            throw new SecretStoreException(
                $"Too many Gemini keys configured; the packed list would exceed the {CredentialManagerStore.MaxBlobBytes} byte credential limit.");
        }
    }

    private static string NormalizeKey(string rawKey)
    {
        var reason = SecretInputRules.Validate(rawKey);
        return reason is null ? rawKey.Trim() : throw new SecretStoreException(reason);
    }

    private static string NewId() => Guid.NewGuid().ToString("N")[..8];

    private static GeminiKeyDto ToDto(GeminiKey key) => new(key.Id, key.Key, key.Last4);

    private static GeminiKey ToKey(GeminiKeyDto dto) => new(dto.Id, dto.Key);

    private sealed record GeminiKeyDto(string Id, string Key, string Last4);
}
