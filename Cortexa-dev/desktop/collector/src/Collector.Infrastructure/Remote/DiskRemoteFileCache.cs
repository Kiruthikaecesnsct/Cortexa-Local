using System.Security.Cryptography;
using System.Text;
using Collector.Application.Extraction;
using Collector.Application.Ports;
using Collector.Application.Remote;
using Collector.Domain.Enums;
using Collector.Domain.Serialization;
using Collector.Infrastructure.Options;
using Microsoft.Extensions.Options;

namespace Collector.Infrastructure.Remote;

public sealed class DiskRemoteFileCache(IOptions<RemoteSourceOptions> options) : IRemoteFileCache
{
    private const int RepoHashLength = 12;
    private const int BranchHashLength = 8;
    private const int BufferBytes = 81920;
    private const string TempSuffix = ".tmp";

    private readonly Lazy<string> _root = new(() => ResolveRoot(options.Value.CacheRoot));

    public string? ResolvePath(SourceType provider, string repoKey, string branch, string path)
    {
        var segments = RemotePathRules.SafeSegments(path);
        if (segments is null)
        {
            return null;
        }

        var directory = Path.Combine(
            _root.Value,
            provider.ToWire(),
            Hash(repoKey.ToLowerInvariant(), RepoHashLength),
            Hash(branch, BranchHashLength));
        var candidate = Path.GetFullPath(Path.Combine([directory, .. segments]));
        return IsUnderRoot(candidate) ? candidate : null;
    }

    public bool Exists(string localPath) => IsUnderRoot(Path.GetFullPath(localPath)) && File.Exists(localPath);

    public async Task<RemoteCacheWriteResult> WriteAsync(
        string localPath,
        Stream content,
        CancellationToken cancellationToken)
    {
        var target = Path.GetFullPath(localPath);
        if (!IsUnderRoot(target))
        {
            throw new InvalidOperationException("The cache path is outside the cache root.");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        var temp = $"{target}.{Guid.NewGuid():N}{TempSuffix}";
        try
        {
            var written = await CopyCappedAsync(content, temp, cancellationToken);
            if (written > FileContentGuard.MaxFileBytes)
            {
                return new RemoteCacheWriteResult(written, true);
            }

            File.Move(temp, target, overwrite: true);
            return new RemoteCacheWriteResult(written, false);
        }
        finally
        {
            File.Delete(temp);
        }
    }

    private static async Task<long> CopyCappedAsync(Stream content, string temp, CancellationToken cancellationToken)
    {
        var limit = FileContentGuard.MaxFileBytes + 1;
        var buffer = new byte[BufferBytes];
        long total = 0;
        await using var output = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, BufferBytes, true);
        while (total < limit)
        {
            var wanted = (int)Math.Min(buffer.Length, limit - total);
            var read = await content.ReadAsync(buffer.AsMemory(0, wanted), cancellationToken);
            if (read == 0)
            {
                break;
            }

            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            total += read;
        }

        return total;
    }

    private bool IsUnderRoot(string fullPath) =>
        fullPath.StartsWith(_root.Value, StringComparison.OrdinalIgnoreCase);

    private static string ResolveRoot(string configured)
    {
        var full = Path.GetFullPath(Environment.ExpandEnvironmentVariables(configured));
        return full.EndsWith(Path.DirectorySeparatorChar) ? full : full + Path.DirectorySeparatorChar;
    }

    private static string Hash(string value, int length) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)))[..length];
}
