using System.Text.Json.Serialization.Metadata;
using Collector.Application.Ports;
using Collector.Application.Remote;
using Collector.Application.Settings;
using Collector.Domain.Enums;
using Collector.Domain.Remote;
using Collector.Infrastructure.Http;
using Collector.Infrastructure.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Collector.Infrastructure.Remote.AzureDevOps;

public sealed class AzureDevOpsRepositoryClient(
    IHttpClientFactory factory,
    IOptionsMonitor<RemoteSourceOptions> options,
    AzureDevOpsErrorMapper mapper,
    ILogger<AzureDevOpsRepositoryClient> logger) : IRemoteRepositoryClient
{
    private const string JsonAccept = "application/json";
    private const string StreamAccept = "application/octet-stream";
    private const string ContinuationHeader = "x-ms-continuationtoken";
    private const string UserAgent = "CortexaCollector";
    private const string HeadsPrefix = "refs/heads/";
    private const string FallbackBranch = "main";
    private const string BlobType = "blob";
    private const string PublicVisibility = "public";
    private static readonly DateTime UnsetDate = DateTime.MinValue.AddDays(1);

    public SourceType Provider => SourceType.AzureDevops;

    public async Task<IReadOnlyList<RemoteRepository>> ListRepositoriesAsync(
        string? scope,
        CancellationToken cancellationToken)
    {
        var organization = RequireOrganization(scope);
        var wire = await ListAsync(
            $"{RemoteUrl.Segment(organization)}/_apis/git/repositories?{ApiVersion()}",
            AzureDevOpsJson.Default.AzureDevOpsListWireAzureDevOpsRepoWire,
            cancellationToken);
        return [.. wire.Select(item => ToRepository(organization, item)).OfType<RemoteRepository>()];
    }

    public async Task<IReadOnlyList<RemoteBranch>> ListBranchesAsync(
        RemoteRepository repository,
        CancellationToken cancellationToken)
    {
        var wire = await ListAsync(
            $"{RepoPath(repository)}/refs?filter=heads/&{ApiVersion()}",
            AzureDevOpsJson.Default.AzureDevOpsListWireAzureDevOpsRefWire,
            cancellationToken);
        return [.. wire.Select(ToBranch).OfType<RemoteBranch>()];
    }

    public async Task<RemoteTree> GetTreeAsync(
        RemoteRepository repository,
        string branch,
        CancellationToken cancellationToken)
    {
        var path =
            $"{RepoPath(repository)}/items?recursionLevel=Full&versionDescriptor.version={RemoteUrl.Segment(branch)}"
            + $"&versionDescriptor.versionType=branch&{ApiVersion()}";
        try
        {
            var items = await ListAsync(path, AzureDevOpsJson.Default.AzureDevOpsListWireAzureDevOpsItemWire, cancellationToken);
            var entries = items.Select(ToEntry).OfType<RemoteTreeEntry>().ToList();
            var commit = items.Select(item => item.CommitId).FirstOrDefault(id => !string.IsNullOrEmpty(id));
            return new RemoteTree(commit ?? string.Empty, entries, false);
        }
        catch (RemoteSourceException exception) when (exception.Kind == RemoteFailureKind.NotFound)
        {
            throw new RemoteSourceException(RemoteFailureKind.EmptyRepository, Provider, null, exception);
        }
    }

    public async Task<RemoteBlob> OpenBlobAsync(
        RemoteRepository repository,
        string blobSha,
        CancellationToken cancellationToken)
    {
        var path = $"{RepoPath(repository)}/blobs/{RemoteUrl.Segment(blobSha)}?$format=octetstream&{ApiVersion()}";
        using var request = NewRequest(RemoteUrl.Relative(path), StreamAccept);
        return await CreateHttp().OpenBlobAsync(request, cancellationToken);
    }

    private static string RequireOrganization(string? scope) =>
        RemoteSourceRules.IsValidOrganization(scope)
            ? scope!
            : throw new RemoteSourceException(RemoteFailureKind.NotFound, SourceType.AzureDevops);

    private static string RepoPath(RemoteRepository repository) =>
        $"{RemoteUrl.Segment(repository.Owner)}/{RemoteUrl.Segment(repository.Project ?? string.Empty)}"
        + $"/_apis/git/repositories/{RemoteUrl.Segment(repository.Name)}";

    private static RemoteRepository? ToRepository(string organization, AzureDevOpsRepoWire wire)
    {
        if (string.IsNullOrEmpty(wire.Name) || string.IsNullOrEmpty(wire.Project?.Name) || wire.IsDisabled == true)
        {
            return null;
        }

        return new RemoteRepository(
            SourceType.AzureDevops,
            organization,
            wire.Project.Name,
            wire.Name,
            $"{organization}/{wire.Project.Name}/{wire.Name}",
            StripHeads(wire.DefaultBranch) ?? FallbackBranch,
            wire.WebUrl ?? string.Empty,
            wire.Size ?? 0,
            !string.Equals(wire.Project.Visibility, PublicVisibility, StringComparison.OrdinalIgnoreCase),
            null,
            ToUpdatedAt(wire.Project.LastUpdateTime));
    }

    private static DateTimeOffset? ToUpdatedAt(DateTimeOffset? value) =>
        value is { } time && time.UtcDateTime > UnsetDate ? time : null;

    private static RemoteBranch? ToBranch(AzureDevOpsRefWire wire) =>
        StripHeads(wire.Name) is { Length: > 0 } name && !string.IsNullOrEmpty(wire.ObjectId)
            ? new RemoteBranch(name, wire.ObjectId)
            : null;

    private static RemoteTreeEntry? ToEntry(AzureDevOpsItemWire wire)
    {
        var isBlob = wire.IsFolder != true && wire.GitObjectType == BlobType;
        var path = wire.Path?.TrimStart('/');
        return isBlob && !string.IsNullOrEmpty(path) && !string.IsNullOrEmpty(wire.ObjectId)
            ? new RemoteTreeEntry(path, wire.ObjectId, null)
            : null;
    }

    private static string? StripHeads(string? reference) =>
        reference is not null && reference.StartsWith(HeadsPrefix, StringComparison.Ordinal)
            ? reference[HeadsPrefix.Length..]
            : null;

    private static HttpRequestMessage NewRequest(Uri uri, string accept)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.TryAddWithoutValidation("Accept", accept);
        request.Headers.UserAgent.ParseAdd(UserAgent);
        return request;
    }

    private static async Task<RemotePage<T>> FetchPageAsync<T>(
        RemoteHttp http,
        PageRequest<T> page,
        CancellationToken cancellationToken)
    {
        using var request = NewRequest(page.Uri, JsonAccept);
        request.Options.Set(RemoteRequestOptions.BufferBody, true);
        using var response = await http.SendCheckedAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken);
        var body = await http.ReadJsonAsync(response, page.TypeInfo, cancellationToken);
        return new RemotePage<T>(body.Value ?? [], NextPage(page.Relative, response));
    }

    private static Uri? NextPage(string relative, HttpResponseMessage response) =>
        response.Headers.TryGetValues(ContinuationHeader, out var values) && values.FirstOrDefault() is { Length: > 0 } token
            ? RemoteUrl.Relative($"{relative}&continuationToken={Uri.EscapeDataString(token)}")
            : null;

    private string ApiVersion() => $"api-version={Uri.EscapeDataString(options.CurrentValue.AzureDevOps.ApiVersion)}";

    private RemoteHttp CreateHttp() =>
        new(factory.CreateClient(HttpClientNames.AzureDevOps), mapper, SourceType.AzureDevops, ReadIdleTimeout());

    private TimeSpan ReadIdleTimeout() => TimeSpan.FromSeconds(options.CurrentValue.TimeoutSeconds);

    private async Task<IReadOnlyList<T>> ListAsync<T>(
        string relative,
        JsonTypeInfo<AzureDevOpsListWire<T>> typeInfo,
        CancellationToken cancellationToken)
    {
        var http = CreateHttp();
        var pager = new RemotePager(options.CurrentValue.MaxPages, logger);
        return await pager.CollectAsync(
            RemoteUrl.Relative(relative),
            (uri, token) => FetchPageAsync(http, new PageRequest<T>(relative, uri, typeInfo), token),
            cancellationToken);
    }

    private sealed record PageRequest<T>(
        string Relative,
        Uri Uri,
        JsonTypeInfo<AzureDevOpsListWire<T>> TypeInfo);
}
