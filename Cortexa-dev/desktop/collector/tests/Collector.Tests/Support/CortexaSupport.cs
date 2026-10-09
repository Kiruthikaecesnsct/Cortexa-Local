using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.Json;
using Collector.Application.Ports;
using Collector.Application.Remote;
using Collector.Domain.Enums;
using Collector.Domain.Remote;
using Collector.Infrastructure.Remote.Cortexa;
using Microsoft.Extensions.Logging.Abstractions;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace Collector.Tests.Support;

internal sealed class UnknownLengthContent(byte[] bytes) : HttpContent
{
    protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
        stream.WriteAsync(bytes, 0, bytes.Length);

    protected override bool TryComputeLength(out long length)
    {
        length = 0;
        return false;
    }
}

internal sealed class CortexaFixture : IDisposable
{
    public const string Origin = "https://gateway.test/";
    public const long DefaultMaxRepositoryBytes = 500L * 1024 * 1024;

    public CortexaFixture(Func<RecordedCall, HttpResponseMessage> respond, long maxRepositoryBytes = DefaultMaxRepositoryBytes)
    {
        Root = Path.Combine(Path.GetTempPath(), "cortexa-tests-" + Guid.NewGuid().ToString("N"));
        Handler = new RouteHandler(respond);
        var options = RemoteData.Options(source => source.CacheRoot = Root);
        var gateway = new CortexaGatewayHttp(
            new BaseAddressClientFactory(Handler, Origin),
            new CortexaErrorMapper(TimeProvider.System));
        Store = new CortexaArchiveStore(
            gateway,
            options,
            MsOptions.Create(new RemoteFetchOptions { MaxRepositoryBytes = maxRepositoryBytes }),
            NullLogger<CortexaArchiveStore>.Instance);
        Client = new CortexaRepositoryClient(gateway, options, Store);
    }

    public string Root { get; }

    public RouteHandler Handler { get; }

    public CortexaArchiveStore Store { get; }

    public CortexaRepositoryClient Client { get; }

    public string ArchiveFolder => Path.Combine(Root, "cortexa_repo", "_zips");

    public string[] ArchiveFiles =>
        Directory.Exists(ArchiveFolder) ? Directory.GetFiles(ArchiveFolder) : [];

    public void Dispose()
    {
        Client.Dispose();
        if (Directory.Exists(Root))
        {
            Directory.Delete(Root, recursive: true);
        }
    }
}

internal static class CortexaData
{
    public const string GitHubTag = "github";
    public const string AzureTag = "azure-devops";
    public const string GitHubClonesPath = "/scan/github/clones";
    public const string AzureClonesPath = "/scan/azure-devops/clones";

    public static byte[] Zip(params (string Name, byte[] Content)[] entries)
    {
        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, content) in entries)
            {
                using var stream = archive.CreateEntry(name).Open();
                stream.Write(content);
            }
        }

        return buffer.ToArray();
    }

    public static byte[] Text(string value) => Encoding.UTF8.GetBytes(value);

    public static object Clone(
        string owner = "octo",
        string repository = "hello",
        string branch = "main",
        string status = "stored",
        string? commitSha = "sha1",
        long sizeBytes = 2048) => new
        {
            clone_id = Guid.NewGuid().ToString("N"),
            provider = "github",
            owner,
            repository,
            branch,
            status,
            created_at = "2026-10-01T00:00:00Z",
            updated_at = "2026-10-02T00:00:00Z",
            size_bytes = sizeBytes,
            commit_sha = commitSha,
            error = (string?)null,
        };

    public static HttpResponseMessage CloneList(params object[] clones) =>
        RemoteResponses.Json(JsonSerializer.Serialize(new { success = true, data = new { clones } }));

    public static HttpResponseMessage FileList(params string[] files) =>
        RemoteResponses.Json(JsonSerializer.Serialize(new { success = true, data = new { files } }));

    public static RemoteRepository Repo(
        string tag = GitHubTag,
        string owner = "octo",
        string name = "hello",
        string branch = "main") => new(
            SourceType.CortexaRepo,
            owner,
            tag,
            name,
            $"{owner}/{name}",
            branch,
            $"cortexa://{tag}/{owner}/{name}",
            0,
            false);
}

internal sealed class CompletingRemoteClient(SourceType provider) : IRemoteRepositoryClient, IRemoteFetchCompletion
{
    private readonly FakeRemoteClient _inner = new(provider);
    private readonly List<(RemoteRepository Repository, string Branch)> _completions = [];

    public SourceType Provider => provider;

    public Exception? OpenError { get; set; }

    public IReadOnlyList<(RemoteRepository Repository, string Branch)> Completions
    {
        get
        {
            lock (_completions)
            {
                return [.. _completions];
            }
        }
    }

    public void SetTree(RemoteTree tree) => _inner.Tree = tree;

    public void AddBlob(string sha, string content) => _inner.AddBlob(sha, content);

    public Task<IReadOnlyList<RemoteRepository>> ListRepositoriesAsync(string? scope, CancellationToken cancellationToken) =>
        _inner.ListRepositoriesAsync(scope, cancellationToken);

    public Task<IReadOnlyList<RemoteBranch>> ListBranchesAsync(RemoteRepository repository, CancellationToken cancellationToken) =>
        _inner.ListBranchesAsync(repository, cancellationToken);

    public Task<RemoteTree> GetTreeAsync(RemoteRepository repository, string branch, CancellationToken cancellationToken) =>
        _inner.GetTreeAsync(repository, branch, cancellationToken);

    public Task<RemoteBlob> OpenBlobAsync(RemoteRepository repository, string blobSha, CancellationToken cancellationToken) =>
        OpenError is null
            ? _inner.OpenBlobAsync(repository, blobSha, cancellationToken)
            : Task.FromException<RemoteBlob>(OpenError);

    public Task CompleteFetchAsync(RemoteRepository repository, string branch)
    {
        lock (_completions)
        {
            _completions.Add((repository, branch));
        }

        return Task.CompletedTask;
    }
}
