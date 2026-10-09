using System.Text.Json.Serialization.Metadata;
using Collector.Application.Ports;
using Collector.Application.Remote;
using Collector.Domain.Enums;
using Collector.Domain.Remote;
using Collector.Infrastructure.Options;
using Microsoft.Extensions.Options;

namespace Collector.Infrastructure.Remote.Cortexa;

public sealed class CortexaRepositoryClient(
    CortexaGatewayHttp gateway,
    IOptionsMonitor<RemoteSourceOptions> options,
    CortexaArchiveStore store) : IRemoteRepositoryClient, IRemoteFetchCompletion, IDisposable
{
    private const string StoredStatus = "stored";
    private const string JsonAccept = "application/json";

    public SourceType Provider => SourceType.CortexaRepo;

    public async Task<IReadOnlyList<RemoteRepository>> ListRepositoriesAsync(
        string? scope,
        CancellationToken cancellationToken)
    {
        var lists = await Task.WhenAll(CortexaCloneRoute.Tags.Select(tag => ListTagAsync(tag, cancellationToken)));
        return [.. lists.SelectMany(list => list)];
    }

    public async Task<IReadOnlyList<RemoteBranch>> ListBranchesAsync(
        RemoteRepository repository,
        CancellationToken cancellationToken)
    {
        var clones = await FetchStoredClonesAsync(RequireTag(repository), cancellationToken);
        var branches = clones
            .Where(clone => Matches(clone, repository))
            .Select(clone => new RemoteBranch(clone.Branch!, VersionOf(clone)))
            .ToList();
        return branches.Count > 0 ? branches : throw NotFound();
    }

    public async Task<RemoteTree> GetTreeAsync(
        RemoteRepository repository,
        string branch,
        CancellationToken cancellationToken)
    {
        var tag = RequireTag(repository);
        var clone = await FindCloneAsync(repository, branch, cancellationToken);
        var files = await FetchFilesAsync(tag, repository, branch, cancellationToken);
        var version = VersionOf(clone);
        var entries = files
            .Where(CanServe)
            .Distinct(StringComparer.Ordinal)
            .Select(path => new RemoteTreeEntry(path, CortexaBlobKeyCodec.Encode(branch, version, path), null))
            .ToList();
        return new RemoteTree(version, entries, Truncated: false);
    }

    public async Task<RemoteBlob> OpenBlobAsync(
        RemoteRepository repository,
        string blobSha,
        CancellationToken cancellationToken)
    {
        var key = ParseKey(blobSha);
        var request = new CortexaArchiveRequest(
            RequireTag(repository),
            repository.Owner,
            repository.Name,
            key.Branch,
            key.Version);
        var zipPath = await store.AcquireAsync(request, cancellationToken);
        return await store.OpenEntryAsync(zipPath, key.RelativePath, cancellationToken);
    }

    public Task CompleteFetchAsync(RemoteRepository repository, string branch) =>
        repository.Project is null
            ? Task.CompletedTask
            : store.ReleaseAsync(CortexaArchiveRequest.KeyPrefix(repository.Project, repository.Owner, repository.Name, branch));

    public void Dispose() => store.Dispose();

    private static RemoteSourceException NotFound() => new(RemoteFailureKind.NotFound, SourceType.CortexaRepo);

    private static RemoteSourceException Malformed() => new(RemoteFailureKind.Upstream, SourceType.CortexaRepo);

    private static string RequireTag(RemoteRepository repository) =>
        repository.Project is { } tag && CortexaCloneRoute.Tags.Contains(tag) ? tag : throw NotFound();

    private static CortexaBlobKey ParseKey(string blobSha)
    {
        try
        {
            return CortexaBlobKeyCodec.Parse(blobSha);
        }
        catch (FormatException exception)
        {
            throw new RemoteSourceException(RemoteFailureKind.Upstream, SourceType.CortexaRepo, null, exception);
        }
    }

    private static bool CanServe(string path) =>
        CortexaBlobKeyCodec.CanEncode(path)
        && !path.Contains('\\')
        && RemotePathRules.SafeSegments(path) is not null;

    private static bool Matches(CortexaCloneWire clone, RemoteRepository repository) =>
        !string.IsNullOrEmpty(clone.Branch)
        && string.Equals(clone.Owner, repository.Owner, StringComparison.OrdinalIgnoreCase)
        && string.Equals(clone.Repository, repository.Name, StringComparison.OrdinalIgnoreCase);

    private static string VersionOf(CortexaCloneWire clone) =>
        string.IsNullOrEmpty(clone.CommitSha) ? clone.UpdatedAt ?? string.Empty : clone.CommitSha;

    private static RemoteRepository? ToRepository(string tag, CortexaCloneWire clone) =>
        string.IsNullOrEmpty(clone.Owner) || string.IsNullOrEmpty(clone.Repository) || string.IsNullOrEmpty(clone.Branch)
            ? null
            : new RemoteRepository(
                SourceType.CortexaRepo,
                clone.Owner,
                tag,
                clone.Repository,
                $"{clone.Owner}/{clone.Repository}",
                clone.Branch,
                $"cortexa://{tag}/{clone.Owner}/{clone.Repository}",
                clone.SizeBytes ?? 0,
                false);

    private async Task<IReadOnlyList<RemoteRepository>> ListTagAsync(string tag, CancellationToken cancellationToken)
    {
        try
        {
            var clones = await FetchStoredClonesAsync(tag, cancellationToken);
            return [.. clones.Select(clone => ToRepository(tag, clone)).OfType<RemoteRepository>()];
        }
        catch (RemoteSourceException exception) when (exception.Kind == RemoteFailureKind.NotFound)
        {
            return [];
        }
    }

    private async Task<CortexaCloneWire> FindCloneAsync(
        RemoteRepository repository,
        string branch,
        CancellationToken cancellationToken)
    {
        var clones = await FetchStoredClonesAsync(RequireTag(repository), cancellationToken);
        return clones.FirstOrDefault(clone =>
            Matches(clone, repository) && string.Equals(clone.Branch, branch, StringComparison.Ordinal))
            ?? throw NotFound();
    }

    private async Task<List<CortexaCloneWire>> FetchStoredClonesAsync(string tag, CancellationToken cancellationToken)
    {
        var prefix = CortexaCloneRoute.Prefix(options.CurrentValue.Cortexa, tag) ?? throw NotFound();
        var data = await GetDataAsync(prefix, CortexaJson.Default.CortexaEnvelopeWireCortexaCloneListWire, cancellationToken);
        return (data.Clones ?? []).Where(clone => clone.Status == StoredStatus).ToList();
    }

    private async Task<List<string>> FetchFilesAsync(
        string tag,
        RemoteRepository repository,
        string branch,
        CancellationToken cancellationToken)
    {
        var prefix = CortexaCloneRoute.Prefix(options.CurrentValue.Cortexa, tag) ?? throw NotFound();
        if (!CortexaCloneRoute.IsQueryable(repository.Owner, repository.Name, branch))
        {
            throw NotFound();
        }

        var path = CortexaCloneRoute.FilesPath(prefix, repository.Owner, repository.Name, branch);
        var data = await GetDataAsync(path, CortexaJson.Default.CortexaEnvelopeWireCortexaFilesWire, cancellationToken);
        return data.Files ?? [];
    }

    private Task<T> GetDataAsync<T>(
        string relative,
        JsonTypeInfo<CortexaEnvelopeWire<T>> typeInfo,
        CancellationToken cancellationToken)
        where T : class =>
        CortexaTimeout.RunAsync(
            options.CurrentValue.TimeoutSeconds,
            token => SendJsonAsync(relative, typeInfo, token),
            cancellationToken);

    private async Task<T> SendJsonAsync<T>(
        string relative,
        JsonTypeInfo<CortexaEnvelopeWire<T>> typeInfo,
        CancellationToken cancellationToken)
        where T : class
    {
        var http = gateway.Create();
        using var request = new HttpRequestMessage(HttpMethod.Get, RemoteUrl.Relative(relative));
        request.Headers.TryAddWithoutValidation("Accept", JsonAccept);
        using var response = await http.SendCheckedAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken);
        var envelope = await http.ReadJsonAsync(response, typeInfo, cancellationToken);
        return envelope.Data ?? throw Malformed();
    }
}
