using System.Collections.Concurrent;
using System.IO.Compression;
using Collector.Application.Remote;
using Collector.Domain.Enums;
using Collector.Infrastructure.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Collector.Infrastructure.Remote.Cortexa;

public sealed record CortexaArchiveRequest(string Tag, string Owner, string Repository, string Branch, string Version)
{
    private const char KeyDelimiter = '|';

    public string Key => $"{KeyPrefix(Tag, Owner, Repository, Branch)}{Version}";

    public static string KeyPrefix(string tag, string owner, string repository, string branch) =>
        string.Join(KeyDelimiter, tag, owner, repository, branch, string.Empty);
}

public sealed class CortexaArchiveStore(
    CortexaGatewayHttp gateway,
    IOptionsMonitor<RemoteSourceOptions> options,
    IOptions<RemoteFetchOptions> fetchOptions,
    ILogger<CortexaArchiveStore> logger) : IDisposable
{
    private const string ArchiveSuffix = ".zip";
    private const string TempSuffix = ".tmp";
    private const string ArchiveRootName = "cortexa_repo";
    private const string ZipMediaType = "application/zip";
    private const int BufferBytes = 81920;

    private readonly ConcurrentDictionary<string, Lazy<Task<string>>> _archives = new();
    private readonly Lazy<string> _folder = new(() => PrepareFolder(options.CurrentValue, logger));

    public async Task<string> AcquireAsync(CortexaArchiveRequest request, CancellationToken cancellationToken)
    {
        var key = request.Key;
        var entry = _archives.GetOrAdd(key, _ => new Lazy<Task<string>>(() => DownloadAsync(request, cancellationToken)));
        try
        {
            return await entry.Value.WaitAsync(cancellationToken);
        }
        catch
        {
            _archives.TryRemove(new KeyValuePair<string, Lazy<Task<string>>>(key, entry));
            throw;
        }
    }

    public async Task<RemoteBlob> OpenEntryAsync(string zipPath, string relativePath, CancellationToken cancellationToken)
    {
        if (!IsServable(relativePath))
        {
            throw NotFound();
        }

        var file = new FileStream(zipPath, new FileStreamOptions
        {
            Mode = FileMode.Open,
            Access = FileAccess.Read,
            Share = FileShare.Read | FileShare.Delete,
            Options = FileOptions.Asynchronous,
        });
        ZipArchive? archive = null;
        try
        {
            archive = await ZipArchive.CreateAsync(file, ZipArchiveMode.Read, false, null, cancellationToken);
            var entry = FindEntry(archive, relativePath);
            return new RemoteBlob(await entry.OpenAsync(cancellationToken), entry.Length, archive);
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException)
        {
            Close(archive, file);
            throw new RemoteSourceException(RemoteFailureKind.Upstream, SourceType.CortexaRepo, null, exception);
        }
        catch
        {
            Close(archive, file);
            throw;
        }
    }

    public async Task ReleaseAsync(string archiveKeyPrefix)
    {
        var keys = _archives.Keys.Where(key => key.StartsWith(archiveKeyPrefix, StringComparison.Ordinal)).ToList();
        foreach (var key in keys)
        {
            if (_archives.TryRemove(key, out var entry))
            {
                await DeleteAsync(entry);
            }
        }
    }

    public void Dispose()
    {
        foreach (var entry in _archives.Values)
        {
            if (entry.IsValueCreated && entry.Value.IsCompletedSuccessfully)
            {
                TryDelete(entry.Value.Result, logger);
            }
        }

        _archives.Clear();
    }

    private static string PrepareFolder(RemoteSourceOptions source, ILogger logger)
    {
        var root = Path.GetFullPath(Environment.ExpandEnvironmentVariables(source.CacheRoot));
        var folder = Path.Combine(root, ArchiveRootName, source.Cortexa.ArchiveFolder);
        Directory.CreateDirectory(folder);
        foreach (var leftover in Directory.EnumerateFiles(folder))
        {
            TryDelete(leftover, logger);
        }

        return folder;
    }

    private static bool IsServable(string relativePath) =>
        RemotePathRules.SafeSegments(relativePath) is not null
        && !relativePath.Contains('\\')
        && !relativePath.Contains(':');

    private static ZipArchiveEntry FindEntry(ZipArchive archive, string relativePath)
    {
        var entry = archive.Entries.FirstOrDefault(candidate =>
            string.Equals(candidate.FullName, relativePath, StringComparison.Ordinal));
        return entry ?? throw NotFound();
    }

    private static RemoteSourceException NotFound() => new(RemoteFailureKind.NotFound, SourceType.CortexaRepo);

    private static RemoteSourceException TooLarge() => new(RemoteFailureKind.RepositoryTooLarge, SourceType.CortexaRepo);

    private static void Close(ZipArchive? archive, FileStream file)
    {
        if (archive is null)
        {
            file.Dispose();
            return;
        }

        archive.Dispose();
    }

    private static void TryDelete(string path, ILogger logger)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(exception, "Could not delete a temporary Cortexa archive.");
        }
    }

    private async Task DeleteAsync(Lazy<Task<string>> entry)
    {
        if (!entry.IsValueCreated)
        {
            return;
        }

        try
        {
            TryDelete(await entry.Value, logger);
        }
        catch (Exception exception) when (exception is RemoteSourceException or OperationCanceledException)
        {
            logger.LogDebug("A Cortexa archive download ended without a file to delete.");
        }
    }

    private Task<string> DownloadAsync(CortexaArchiveRequest request, CancellationToken cancellationToken) =>
        CortexaTimeout.RunAsync(
            options.CurrentValue.Cortexa.DownloadTimeoutSeconds,
            token => DownloadToDiskAsync(request, token),
            cancellationToken);

    private async Task<string> DownloadToDiskAsync(CortexaArchiveRequest request, CancellationToken cancellationToken)
    {
        var target = Path.Combine(_folder.Value, $"{Guid.NewGuid():N}{ArchiveSuffix}");
        var temp = target + TempSuffix;
        try
        {
            using var response = await SendAsync(request, cancellationToken);
            await WriteCappedAsync(response, temp, cancellationToken);
            File.Move(temp, target);
            return target;
        }
        catch (Exception exception) when (exception is IOException or HttpRequestException)
        {
            throw new RemoteSourceException(RemoteFailureKind.Upstream, SourceType.CortexaRepo, null, exception);
        }
        finally
        {
            TryDelete(temp, logger);
        }
    }

    private async Task<HttpResponseMessage> SendAsync(CortexaArchiveRequest request, CancellationToken cancellationToken)
    {
        var prefix = CortexaCloneRoute.Prefix(options.CurrentValue.Cortexa, request.Tag) ?? throw NotFound();
        if (!CortexaCloneRoute.IsQueryable(request.Owner, request.Repository, request.Branch))
        {
            throw NotFound();
        }

        var path = CortexaCloneRoute.DownloadPath(prefix, request.Owner, request.Repository, request.Branch);
        using var message = new HttpRequestMessage(HttpMethod.Get, RemoteUrl.Relative(path));
        message.Headers.TryAddWithoutValidation("Accept", ZipMediaType);
        var response = await gateway.Create().SendCheckedAsync(message, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (response.Content.Headers.ContentLength > fetchOptions.Value.MaxRepositoryBytes)
        {
            response.Dispose();
            throw TooLarge();
        }

        return response;
    }

    private async Task WriteCappedAsync(HttpResponseMessage response, string temp, CancellationToken cancellationToken)
    {
        var limit = fetchOptions.Value.MaxRepositoryBytes;
        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var output = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None, BufferBytes, FileOptions.Asynchronous);
        var buffer = new byte[BufferBytes];
        long total = 0;
        int read;
        while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
        {
            total += read;
            if (total > limit)
            {
                throw TooLarge();
            }

            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }
    }
}
