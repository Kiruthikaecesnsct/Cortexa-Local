using Collector.Application.Secrets;
using Collector.Infrastructure.Secrets;
using Microsoft.Extensions.Logging;
using Microsoft.Identity.Client;

namespace Collector.Infrastructure.Remote.AzureDevOps;

public sealed class MsalCacheBinding(EntraTokenCacheStore store, ILogger<MsalCacheBinding> logger)
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Lock _lock = new();
    private byte[]? _bytes;
    private int _version;

    public void Attach(ITokenCache cache)
    {
        var attachment = new Attachment();
        cache.SetBeforeAccessAsync(args => BeforeAccessAsync(args, attachment));
        cache.SetAfterAccessAsync(args => AfterAccessAsync(args, attachment));
    }

    public async Task ClearAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            Replace([]);
            await store.ClearAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task BeforeAccessAsync(TokenCacheNotificationArgs args, Attachment attachment)
    {
        var (bytes, version) = await SnapshotAsync(args.CancellationToken);
        if (attachment.Seen == version)
        {
            return;
        }

        if (await TryDeserializeAsync(args.TokenCache, bytes))
        {
            attachment.Seen = version;
            return;
        }

        attachment.Seen = CurrentVersion();
    }

    private async Task AfterAccessAsync(TokenCacheNotificationArgs args, Attachment attachment)
    {
        if (!args.HasStateChanged)
        {
            return;
        }

        attachment.Seen = Replace(args.TokenCache.SerializeMsalV3());
        await PersistAsync();
    }

    private async Task<(byte[] Bytes, int Version)> SnapshotAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (Current() is null)
            {
                Replace(await LoadAsync(cancellationToken));
            }

            lock (_lock)
            {
                return (_bytes ?? [], _version);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<byte[]> LoadAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await store.LoadAsync(cancellationToken);
        }
        catch (SecretStoreException exception)
        {
            logger.LogWarning(exception, "Could not read the stored sign-in cache.");
            return [];
        }
    }

    private async Task<bool> TryDeserializeAsync(ITokenCacheSerializer serializer, byte[] bytes)
    {
        try
        {
            serializer.DeserializeMsalV3(bytes, shouldClearExistingCache: true);
            return true;
        }
        catch (Exception exception) when (exception is MsalException or ArgumentException or InvalidOperationException)
        {
            logger.LogWarning("The stored sign-in cache could not be read ({ErrorType}). Starting empty.", exception.GetType().Name);
            await ResetAfterCorruptionAsync();
            return false;
        }
    }

    private async Task ResetAfterCorruptionAsync()
    {
        await _gate.WaitAsync();
        try
        {
            Replace([]);
            await store.ClearAsync(CancellationToken.None);
        }
        catch (SecretStoreException exception)
        {
            logger.LogWarning(exception, "Could not clear the corrupt sign-in cache.");
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task PersistAsync()
    {
        await _gate.WaitAsync();
        try
        {
            await store.SaveAsync(Current() ?? [], CancellationToken.None);
        }
        catch (SecretStoreException exception)
        {
            logger.LogWarning(exception, "Could not store the sign-in cache. It stays in memory for this session.");
        }
        finally
        {
            _gate.Release();
        }
    }

    private byte[]? Current()
    {
        lock (_lock)
        {
            return _bytes;
        }
    }

    private int CurrentVersion()
    {
        lock (_lock)
        {
            return _version;
        }
    }

    private int Replace(byte[] bytes)
    {
        lock (_lock)
        {
            _bytes = bytes;
            return ++_version;
        }
    }

    private sealed class Attachment
    {
        public int Seen { get; set; } = -1;
    }
}
