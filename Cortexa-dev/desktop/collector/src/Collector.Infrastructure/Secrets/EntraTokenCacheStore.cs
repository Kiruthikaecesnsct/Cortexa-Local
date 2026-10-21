using System.Globalization;
using System.Text;
using Collector.Application.Ports;
using Collector.Application.Secrets;
using Microsoft.Extensions.Logging;

namespace Collector.Infrastructure.Secrets;

public sealed class EntraTokenCacheStore(ISecretStore secrets, ILogger<EntraTokenCacheStore> logger)
{
    public const int PartChars = 2560;

    private const string IndexPrefix = "v1:";

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

    public async Task SaveAsync(byte[] cache, CancellationToken cancellationToken)
    {
        if (cache.Length == 0)
        {
            await ClearAsync(cancellationToken);
            return;
        }

        var parts = Split(Convert.ToBase64String(cache));
        if (parts.Count > PartSlots.Length)
        {
            logger.LogWarning("The sign-in cache is {Size} bytes, which is too large to store. It stays in memory for this session.", cache.Length);
            return;
        }

        await WriteAsync(parts, cancellationToken);
    }

    public async Task<byte[]> LoadAsync(CancellationToken cancellationToken)
    {
        var count = ParseCount(await secrets.ReadAsync(SecretSlot.EntraIdCacheIndex, cancellationToken));
        if (count == 0)
        {
            return [];
        }

        var joined = await ReadPartsAsync(count, cancellationToken);
        var decoded = joined is null ? null : Decode(joined);
        if (decoded is not null)
        {
            return decoded;
        }

        await ClearAsync(cancellationToken);
        return [];
    }

    public async Task ClearAsync(CancellationToken cancellationToken)
    {
        SecretStoreException? firstFailure = null;
        foreach (var slot in PartSlots.Prepend(SecretSlot.EntraIdCacheIndex))
        {
            var failure = await TryDeleteAsync(slot, cancellationToken);
            firstFailure ??= failure;
        }

        if (firstFailure is not null)
        {
            throw firstFailure;
        }
    }

    private static List<string> Split(string encoded)
    {
        var parts = new List<string>();
        for (var offset = 0; offset < encoded.Length; offset += PartChars)
        {
            parts.Add(encoded.Substring(offset, Math.Min(PartChars, encoded.Length - offset)));
        }

        return parts;
    }

    private static int ParseCount(string? index)
    {
        if (index is null || !index.StartsWith(IndexPrefix, StringComparison.Ordinal))
        {
            return 0;
        }

        var parsed = int.TryParse(index.AsSpan(IndexPrefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out var count);
        return parsed && count >= 1 && count <= PartSlots.Length ? count : 0;
    }

    private static byte[]? Decode(string joined)
    {
        try
        {
            return Convert.FromBase64String(joined);
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private async Task WriteAsync(List<string> parts, CancellationToken cancellationToken)
    {
        for (var i = 0; i < parts.Count; i++)
        {
            await secrets.WriteAsync(PartSlots[i], parts[i], cancellationToken);
        }

        await secrets.WriteAsync(SecretSlot.EntraIdCacheIndex, $"{IndexPrefix}{parts.Count}", cancellationToken);
        for (var i = parts.Count; i < PartSlots.Length; i++)
        {
            await secrets.DeleteAsync(PartSlots[i], cancellationToken);
        }
    }

    private async Task<string?> ReadPartsAsync(int count, CancellationToken cancellationToken)
    {
        var builder = new StringBuilder();
        for (var i = 0; i < count; i++)
        {
            var part = await secrets.ReadAsync(PartSlots[i], cancellationToken);
            if (string.IsNullOrEmpty(part))
            {
                return null;
            }

            builder.Append(part);
        }

        return builder.ToString();
    }

    private async Task<SecretStoreException?> TryDeleteAsync(SecretSlot slot, CancellationToken cancellationToken)
    {
        try
        {
            await secrets.DeleteAsync(slot, cancellationToken);
            return null;
        }
        catch (SecretStoreException exception)
        {
            return exception;
        }
    }
}
