using System.Net;
using Collector.Application.Remote;
using Collector.Domain.Enums;
using Collector.Domain.Remote;
using Collector.Infrastructure.Http;
using Collector.Infrastructure.Options;
using Collector.Infrastructure.Remote.AzureDevOps;
using Collector.Infrastructure.Remote.RateLimit;
using Collector.Tests.Support;
using Microsoft.Extensions.Logging.Abstractions;

namespace Collector.Tests.Remote;

public class AzureDevOpsRepositoryClientTests
{
    private const string Origin = "https://dev.azure.com";
    private const string ApiVersion = "api-version=7.1";
    private const string RepoRoot = "https://dev.azure.com/myorg/proj/_apis/git/repositories/repo";
    private const long RepoSize = 2048;

    private static AzureDevOpsRepositoryClient CreateClient(
        RouteHandler handler,
        Action<RemoteSourceOptions>? configure = null,
        BaseAddressClientFactory? factory = null) => new(
            factory ?? new BaseAddressClientFactory(handler, $"{Origin}/"),
            RemoteData.Options(configure),
            new AzureDevOpsErrorMapper(new AzureDevOpsRateHeaders(), TimeProvider.System),
            NullLogger<AzureDevOpsRepositoryClient>.Instance);

    private static string RepoJson(string name, string visibility = "private", bool disabled = false, string branch = "refs/heads/dev", string? lastUpdate = null) =>
        $$"""
        {"name":"{{name}}","defaultBranch":"{{branch}}","webUrl":"https://dev.azure.com/myorg/proj/_git/{{name}}",
         "size":{{RepoSize}},"isDisabled":{{(disabled ? "true" : "false")}},
         "project":{"name":"proj","visibility":"{{visibility}}"{{LastUpdateJson(lastUpdate)}} }
        }
        """;

    private static string LastUpdateJson(string? value) => value is null ? string.Empty : $",\"lastUpdateTime\":\"{value}\"";

    [Fact]
    public async Task ListRepositoriesAsync_ValidResponse_MapsRepositoriesAndUsesNamedClient()
    {
        var handler = new RouteHandler(_ => RemoteResponses.Json($$"""{"value":[{{RepoJson("repo")}}]}"""));
        var factory = new BaseAddressClientFactory(handler, $"{Origin}/");

        var repositories = await CreateClient(handler, factory: factory).ListRepositoriesAsync("myorg", TestSupport.Ct);

        Assert.Equal(
            new RemoteRepository(
                SourceType.AzureDevops,
                "myorg",
                "proj",
                "repo",
                "myorg/proj/repo",
                "dev",
                "https://dev.azure.com/myorg/proj/_git/repo",
                RepoSize,
                true),
            Assert.Single(repositories));
        Assert.Equal($"{Origin}/myorg/_apis/git/repositories?{ApiVersion}", Assert.Single(handler.Calls).Uri.AbsoluteUri);
        Assert.Equal("application/json", Assert.Single(handler.Calls).Headers["Accept"]);
        Assert.Equal(HttpClientNames.AzureDevOps, factory.LastName);
    }

    [Fact]
    public async Task ListRepositoriesAsync_PublicProject_IsNotPrivate()
    {
        var handler = new RouteHandler(_ => RemoteResponses.Json($$"""{"value":[{{RepoJson("repo", "public")}}]}"""));

        var repositories = await CreateClient(handler).ListRepositoriesAsync("myorg", TestSupport.Ct);

        Assert.False(Assert.Single(repositories).IsPrivate);
    }

    [Fact]
    public async Task ListRepositoriesAsync_DisabledRepository_IsDropped()
    {
        var handler = new RouteHandler(_ => RemoteResponses.Json(
            $$"""{"value":[{{RepoJson("off", disabled: true)}},{{RepoJson("on")}}]}"""));

        var repositories = await CreateClient(handler).ListRepositoriesAsync("myorg", TestSupport.Ct);

        Assert.Equal(["on"], repositories.Select(repository => repository.Name));
    }

    [Fact]
    public async Task ListRepositoriesAsync_DefaultBranchOutsideHeads_FallsBackToMain()
    {
        var handler = new RouteHandler(_ => RemoteResponses.Json(
            $$"""{"value":[{{RepoJson("repo", branch: "refs/tags/v1")}}]}"""));

        var repositories = await CreateClient(handler).ListRepositoriesAsync("myorg", TestSupport.Ct);

        Assert.Equal("main", Assert.Single(repositories).DefaultBranch);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("bad org")]
    [InlineData("-leading")]
    [InlineData("../other")]
    public async Task ListRepositoriesAsync_InvalidOrganization_ThrowsNotFoundWithoutRequest(string? scope)
    {
        var handler = new RouteHandler(_ => RemoteResponses.Json("{}"));

        var exception = await Assert.ThrowsAsync<RemoteSourceException>(
            () => CreateClient(handler).ListRepositoriesAsync(scope, TestSupport.Ct));

        Assert.Equal(RemoteFailureKind.NotFound, exception.Kind);
        Assert.Empty(handler.Calls);
    }

    [Fact]
    public async Task ListRepositoriesAsync_ContinuationToken_FetchesNextPageWithEscapedToken()
    {
        var handler = new RouteHandler(call => call.Uri.Query.Contains("continuationToken", StringComparison.Ordinal)
            ? RemoteResponses.Json($$"""{"value":[{{RepoJson("second")}}]}""")
            : RemoteResponses.Json($$"""{"value":[{{RepoJson("first")}}]}""", ("x-ms-continuationtoken", "tok en/1")));

        var repositories = await CreateClient(handler).ListRepositoriesAsync("myorg", TestSupport.Ct);

        Assert.Equal(["first", "second"], repositories.Select(repository => repository.Name));
        Assert.Equal(
            $"{Origin}/myorg/_apis/git/repositories?{ApiVersion}&continuationToken=tok%20en%2F1",
            handler.Calls[1].Uri.AbsoluteUri);
    }

    [Fact]
    public async Task ListRepositoriesAsync_MaxPagesReached_StopsFollowingTokens()
    {
        const int MaxPages = 3;
        var handler = new RouteHandler(_ => RemoteResponses.Json($$"""{"value":[{{RepoJson("r")}}]}""", ("x-ms-continuationtoken", "more")));

        await CreateClient(handler, options => options.MaxPages = MaxPages).ListRepositoriesAsync("myorg", TestSupport.Ct);

        Assert.Equal(MaxPages, handler.Calls.Count);
    }

    [Theory]
    [InlineData(HttpStatusCode.NonAuthoritativeInformation)]
    [InlineData(HttpStatusCode.Found)]
    [InlineData(HttpStatusCode.Unauthorized)]
    public async Task ListRepositoriesAsync_SignInPageOrUnauthorized_ThrowsAuth(HttpStatusCode status)
    {
        var handler = new RouteHandler(_ => RemoteResponses.Status(status));

        var exception = await Assert.ThrowsAsync<RemoteSourceException>(
            () => CreateClient(handler).ListRepositoriesAsync("myorg", TestSupport.Ct));

        Assert.Equal(RemoteFailureKind.Auth, exception.Kind);
    }

    [Fact]
    public async Task ListBranchesAsync_ValidResponse_StripsHeadsPrefixAndDropsOtherRefs()
    {
        const string Body = """
            {"value":[{"name":"refs/heads/main","objectId":"a1"},{"name":"refs/tags/v1","objectId":"b2"},
                      {"name":"refs/heads/nosha"}]}
            """;
        var handler = new RouteHandler(_ => RemoteResponses.Json(Body));

        var branches = await CreateClient(handler).ListBranchesAsync(RemoteData.AzureRepo(), TestSupport.Ct);

        Assert.Equal([new RemoteBranch("main", "a1")], branches);
        Assert.Equal($"{RepoRoot}/refs?filter=heads/&{ApiVersion}", Assert.Single(handler.Calls).Uri.AbsoluteUri);
    }

    [Fact]
    public async Task GetTreeAsync_ValidResponse_KeepsBlobsTrimsLeadingSlashAndTakesCommit()
    {
        const string Body = """
            {"value":[
              {"objectId":"root","gitObjectType":"tree","commitId":"","path":"/","isFolder":true},
              {"objectId":"b1","gitObjectType":"blob","commitId":"c9","path":"/docs/readme.md"},
              {"objectId":"t1","gitObjectType":"tree","commitId":"c9","path":"/docs","isFolder":true},
              {"objectId":"b2","gitObjectType":"blob","commitId":"c9","path":"/src/a.cs"}]}
            """;
        var handler = new RouteHandler(_ => RemoteResponses.Json(Body));

        var tree = await CreateClient(handler).GetTreeAsync(RemoteData.AzureRepo(), "feat x", TestSupport.Ct);

        Assert.Equal("c9", tree.CommitSha);
        Assert.False(tree.Truncated);
        Assert.Equal(
            [new RemoteTreeEntry("docs/readme.md", "b1", null), new RemoteTreeEntry("src/a.cs", "b2", null)],
            tree.Entries);
        Assert.Equal(
            $"{RepoRoot}/items?recursionLevel=Full&versionDescriptor.version=feat%20x&versionDescriptor.versionType=branch&{ApiVersion}",
            Assert.Single(handler.Calls).Uri.AbsoluteUri);
    }

    [Fact]
    public async Task GetTreeAsync_NotFound_ThrowsEmptyRepository()
    {
        var handler = new RouteHandler(_ => RemoteResponses.Status(HttpStatusCode.NotFound));

        var exception = await Assert.ThrowsAsync<RemoteSourceException>(
            () => CreateClient(handler).GetTreeAsync(RemoteData.AzureRepo(), "main", TestSupport.Ct));

        Assert.Equal(RemoteFailureKind.EmptyRepository, exception.Kind);
    }

    [Fact]
    public async Task GetTreeAsync_Forbidden_ThrowsAccessDeniedNotEmpty()
    {
        var handler = new RouteHandler(_ => RemoteResponses.Status(HttpStatusCode.Forbidden));

        var exception = await Assert.ThrowsAsync<RemoteSourceException>(
            () => CreateClient(handler).GetTreeAsync(RemoteData.AzureRepo(), "main", TestSupport.Ct));

        Assert.Equal(RemoteFailureKind.AccessDenied, exception.Kind);
    }

    [Fact]
    public async Task OpenBlobAsync_ValidBlob_StreamsOctetStream()
    {
        const string Content = "print('hi')";
        var handler = new RouteHandler(_ => RemoteResponses.Text(Content));

        using var blob = await CreateClient(handler).OpenBlobAsync(RemoteData.AzureRepo(), "b1", TestSupport.Ct);
        using var reader = new StreamReader(blob.Content);

        Assert.Equal(Content, await reader.ReadToEndAsync(TestSupport.Ct));
        var call = Assert.Single(handler.Calls);
        Assert.Equal($"{RepoRoot}/blobs/b1?$format=octetstream&{ApiVersion}", call.Uri.AbsoluteUri);
        Assert.Equal("application/octet-stream", call.Headers["Accept"]);
    }

    [Fact]
    public async Task OpenBlobAsync_SignInPage_ThrowsAuth()
    {
        var handler = new RouteHandler(_ => RemoteResponses.Status(HttpStatusCode.NonAuthoritativeInformation));

        var exception = await Assert.ThrowsAsync<RemoteSourceException>(
            () => CreateClient(handler).OpenBlobAsync(RemoteData.AzureRepo(), "b1", TestSupport.Ct));

        Assert.Equal(RemoteFailureKind.Auth, exception.Kind);
    }

    [Fact]
    public async Task ListRepositoriesAsync_LastUpdateTimePresent_SetsUpdatedAt()
    {
        const string Stamp = "2026-03-04T05:06:07Z";
        var handler = new RouteHandler(_ => RemoteResponses.Json($$"""{"value":[{{RepoJson("repo", lastUpdate: Stamp)}}]}"""));

        var repositories = await CreateClient(handler).ListRepositoriesAsync("myorg", TestSupport.Ct);

        Assert.Equal(DateTimeOffset.Parse(Stamp), Assert.Single(repositories).UpdatedAt);
    }

    [Fact]
    public async Task ListRepositoriesAsync_LastUpdateTimeAbsent_LeavesUpdatedAtNull()
    {
        var handler = new RouteHandler(_ => RemoteResponses.Json($$"""{"value":[{{RepoJson("repo")}}]}"""));

        var repositories = await CreateClient(handler).ListRepositoriesAsync("myorg", TestSupport.Ct);

        Assert.Null(Assert.Single(repositories).UpdatedAt);
    }

    [Fact]
    public async Task ListRepositoriesAsync_YearOneLastUpdateTime_LeavesUpdatedAtNull()
    {
        const string Unset = "0001-01-01T00:00:00Z";
        var handler = new RouteHandler(_ => RemoteResponses.Json($$"""{"value":[{{RepoJson("repo", lastUpdate: Unset)}}]}"""));

        var repositories = await CreateClient(handler).ListRepositoriesAsync("myorg", TestSupport.Ct);

        Assert.Null(Assert.Single(repositories).UpdatedAt);
    }

    [Fact]
    public async Task GetTreeAsync_BlobItems_HaveNullSizeBytes()
    {
        const string Body = """{"value":[{"objectId":"b1","gitObjectType":"blob","commitId":"c9","path":"/a.md"}]}""";
        var handler = new RouteHandler(_ => RemoteResponses.Json(Body));

        var tree = await CreateClient(handler).GetTreeAsync(RemoteData.AzureRepo(), "main", TestSupport.Ct);

        Assert.Null(Assert.Single(tree.Entries).SizeBytes);
    }

    [Fact]
    public async Task PageRequests_Always_CarryBufferBodyOption()
    {
        const string TreeBody = """{"value":[{"objectId":"b1","gitObjectType":"blob","commitId":"c9","path":"/a.md"}]}""";
        var repoHandler = new RouteHandler(_ => RemoteResponses.Json($$"""{"value":[{{RepoJson("repo")}}]}"""));
        var treeHandler = new RouteHandler(_ => RemoteResponses.Json(TreeBody));

        await CreateClient(repoHandler).ListRepositoriesAsync("myorg", TestSupport.Ct);
        await CreateClient(treeHandler).GetTreeAsync(RemoteData.AzureRepo(), "main", TestSupport.Ct);

        Assert.True(Assert.Single(repoHandler.Calls).BufferBody);
        Assert.True(Assert.Single(treeHandler.Calls).BufferBody);
    }
}
