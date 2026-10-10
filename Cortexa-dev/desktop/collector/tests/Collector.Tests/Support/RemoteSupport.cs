using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Collector.Application.Ports;
using Collector.Application.Remote;
using Collector.Domain.Enums;
using Collector.Domain.Remote;
using Collector.Infrastructure.Options;

namespace Collector.Tests.Support;

internal sealed record RecordedCall(Uri Uri, IReadOnlyDictionary<string, string> Headers)
{
    public bool BufferBody { get; init; }
}

internal sealed class RouteHandler(Func<RecordedCall, HttpResponseMessage> respond) : HttpMessageHandler
{
    private readonly List<RecordedCall> _calls = [];

    public IReadOnlyList<RecordedCall> Calls
    {
        get
        {
            lock (_calls)
            {
                return [.. _calls];
            }
        }
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var headers = request.Headers.ToDictionary(
            header => header.Key,
            header => string.Join(',', header.Value),
            StringComparer.OrdinalIgnoreCase);
        var call = new RecordedCall(request.RequestUri!, headers)
        {
            BufferBody = request.Options.TryGetValue(Collector.Infrastructure.Remote.RemoteRequestOptions.BufferBody, out var buffer) && buffer,
        };
        lock (_calls)
        {
            _calls.Add(call);
        }

        var response = respond(call);
        response.RequestMessage = request;
        return Task.FromResult(response);
    }
}

internal sealed class BaseAddressClientFactory(HttpMessageHandler handler, string baseAddress) : IHttpClientFactory
{
    public string? LastName { get; private set; }

    public HttpClient CreateClient(string name)
    {
        LastName = name;
        return new HttpClient(handler, disposeHandler: false) { BaseAddress = new Uri(baseAddress) };
    }
}

internal static class RemoteResponses
{
    public static HttpResponseMessage Json(string body, params (string Name, string Value)[] headers) =>
        With(StubHttpHandler.Json(HttpStatusCode.OK, body), headers);

    public static HttpResponseMessage Status(HttpStatusCode status, params (string Name, string Value)[] headers) =>
        With(new HttpResponseMessage(status), headers);

    public static HttpResponseMessage Bytes(byte[] content) =>
        new(HttpStatusCode.OK) { Content = new ByteArrayContent(content) };

    public static HttpResponseMessage Text(string content) => Bytes(Encoding.UTF8.GetBytes(content));

    public static HttpResponseMessage RetryAfter(HttpStatusCode status, TimeSpan delay)
    {
        var response = new HttpResponseMessage(status);
        response.Headers.RetryAfter = new RetryConditionHeaderValue(delay);
        return response;
    }

    private static HttpResponseMessage With(HttpResponseMessage response, (string Name, string Value)[] headers)
    {
        foreach (var (name, value) in headers)
        {
            response.Headers.TryAddWithoutValidation(name, value);
        }

        return response;
    }
}

internal static class RemoteData
{
    public static RemoteRepository GitHubRepo(long sizeBytes = 0) =>
        new(SourceType.Github, "octo", null, "hello", "octo/hello", "main", "https://github.com/octo/hello", sizeBytes, false);

    public static RemoteRepository AzureRepo(long sizeBytes = 0) =>
        new(SourceType.AzureDevops, "myorg", "proj", "repo", "myorg/proj/repo", "main", "https://dev.azure.com/myorg/proj/_git/repo", sizeBytes, true);

    public static RemoteTreeEntry Entry(string path, string sha, long? size = null) => new(path, sha, size);

    public static RemoteFileRecord Record(
        RemoteRepository repository,
        string? branch,
        string path,
        string blobSha,
        string localPath = "cache/file") => new(
            Guid.NewGuid().ToString("n"),
            repository.Provider,
            repository.WebUrl,
            repository.RepoKey,
            branch,
            "commit-1",
            path,
            blobSha,
            10,
            localPath,
            DateTimeOffset.Parse("2026-10-07T00:00:00Z"));

    public static StaticMonitor<RemoteSourceOptions> Options(Action<RemoteSourceOptions>? configure = null)
    {
        var options = new RemoteSourceOptions();
        configure?.Invoke(options);
        return new StaticMonitor<RemoteSourceOptions>(options);
    }
}

internal sealed class FakeRemoteClient(SourceType provider) : IRemoteRepositoryClient
{
    private readonly Dictionary<string, byte[]> _blobs = [];
    private readonly List<string> _openedBlobs = [];

    public SourceType Provider => provider;

    public RemoteTree Tree { get; set; } = new("commit-1", [], false);

    public int TreeCalls { get; private set; }

    public long? LengthOverride { get; set; }

    public IReadOnlyList<string> OpenedBlobs
    {
        get
        {
            lock (_openedBlobs)
            {
                return [.. _openedBlobs];
            }
        }
    }

    public void AddBlob(string sha, string content) => _blobs[sha] = Encoding.UTF8.GetBytes(content);

    public Task<IReadOnlyList<RemoteRepository>> ListRepositoriesAsync(string? scope, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<RemoteRepository>>([]);

    public Task<IReadOnlyList<RemoteBranch>> ListBranchesAsync(RemoteRepository repository, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<RemoteBranch>>([]);

    public Task<RemoteTree> GetTreeAsync(RemoteRepository repository, string branch, CancellationToken cancellationToken)
    {
        TreeCalls++;
        return Task.FromResult(Tree);
    }

    public Task<RemoteBlob> OpenBlobAsync(RemoteRepository repository, string blobSha, CancellationToken cancellationToken)
    {
        lock (_openedBlobs)
        {
            _openedBlobs.Add(blobSha);
        }

        var bytes = _blobs.TryGetValue(blobSha, out var found) ? found : [];
        return Task.FromResult(new RemoteBlob(new MemoryStream(bytes), LengthOverride ?? bytes.Length));
    }
}

internal sealed class FakeRemoteClients(params IRemoteRepositoryClient[] clients) : IRemoteRepositoryClients
{
    public IRemoteRepositoryClient For(SourceType provider) => clients.First(client => client.Provider == provider);
}

internal sealed class InMemoryRemoteFileStore : IRemoteFileStore
{
    private readonly Dictionary<string, RemoteFileRecord> _records = [];

    public IReadOnlyList<RemoteFileRecord> All
    {
        get
        {
            lock (_records)
            {
                return [.. _records.Values];
            }
        }
    }

    public Task UpsertAsync(RemoteFileRecord record, CancellationToken cancellationToken)
    {
        lock (_records)
        {
            _records[Key(record.Provider, record.RepoKey, record.Branch, record.Path)] = record;
        }

        return Task.CompletedTask;
    }

    public Task<RemoteFileRecord?> GetAsync(SourceType provider, string repoKey, string? branch, string path, CancellationToken cancellationToken)
    {
        lock (_records)
        {
            return Task.FromResult(_records.GetValueOrDefault(Key(provider, repoKey, branch, path)));
        }
    }

    public Task<IReadOnlyList<RemoteFileRecord>> ListAsync(SourceType provider, string repoKey, string? branch, CancellationToken cancellationToken)
    {
        lock (_records)
        {
            return Task.FromResult<IReadOnlyList<RemoteFileRecord>>(
                [.. _records.Values.Where(r => r.Provider == provider && r.RepoKey == repoKey && r.Branch == branch)]);
        }
    }

    public Task DeleteAsync(SourceType provider, string repoKey, string? branch, string path, CancellationToken cancellationToken)
    {
        lock (_records)
        {
            _records.Remove(Key(provider, repoKey, branch, path));
        }

        return Task.CompletedTask;
    }

    private static string Key(SourceType provider, string repoKey, string? branch, string path) =>
        $"{provider}|{repoKey}|{branch}|{path}";
}

internal sealed class InMemoryRemoteFileCache : IRemoteFileCache
{
    private readonly HashSet<string> _existing = [];
    private readonly List<string> _written = [];

    public bool ExceedLimit { get; set; }

    public IReadOnlyList<string> Written
    {
        get
        {
            lock (_written)
            {
                return [.. _written];
            }
        }
    }

    public void MarkExisting(SourceType provider, string repoKey, string branch, string path) =>
        _existing.Add(ResolvePath(provider, repoKey, branch, path)!);

    public string? ResolvePath(SourceType provider, string repoKey, string branch, string path) =>
        path.Contains("..", StringComparison.Ordinal) ? null : $"cache/{provider}/{repoKey}/{branch}/{path}";

    public bool Exists(string localPath)
    {
        lock (_written)
        {
            return _existing.Contains(localPath) || _written.Contains(localPath);
        }
    }

    public async Task<RemoteCacheWriteResult> WriteAsync(string localPath, Stream content, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, cancellationToken);
        if (ExceedLimit)
        {
            return new RemoteCacheWriteResult(buffer.Length, true);
        }

        lock (_written)
        {
            _written.Add(localPath);
        }

        return new RemoteCacheWriteResult(buffer.Length, false);
    }
}

internal sealed class ListProgress<T> : IProgress<T>
{
    private readonly List<T> _items = [];

    public IReadOnlyList<T> Items
    {
        get
        {
            lock (_items)
            {
                return [.. _items];
            }
        }
    }

    public void Report(T value)
    {
        lock (_items)
        {
            _items.Add(value);
        }
    }
}
