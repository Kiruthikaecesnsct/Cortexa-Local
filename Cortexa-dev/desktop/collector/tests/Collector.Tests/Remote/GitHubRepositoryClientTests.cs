using System.Net;
using Collector.Application.Remote;
using Collector.Domain.Enums;
using Collector.Domain.Remote;
using Collector.Infrastructure.Http;
using Collector.Infrastructure.Options;
using Collector.Infrastructure.Remote.GitHub;
using Collector.Infrastructure.Remote.RateLimit;
using Collector.Tests.Support;
using Microsoft.Extensions.Logging.Abstractions;

namespace Collector.Tests.Remote;

public class GitHubRepositoryClientTests
{
    private const string Origin = "https://api.github.com";
    private const string RepoRoot = "https://api.github.com/repos/octo/hello";
    private const string TreeSha = "tree-sha";
    private const string CommitSha = "commit-sha";
    private const long SizeKilobytes = 3;
    private const long BytesPerKilobyte = 1024;
    private const string ReposPath = "/user/repos?affiliation=owner,collaborator,organization_member&per_page=100";

    private static GitHubRepositoryClient CreateClient(
        RouteHandler handler,
        Action<RemoteSourceOptions>? configure = null,
        BaseAddressClientFactory? factory = null) => new(
            factory ?? new BaseAddressClientFactory(handler, $"{Origin}/"),
            RemoteData.Options(configure),
            new GitHubErrorMapper(new GitHubRateHeaders(), TimeProvider.System),
            NullLogger<GitHubRepositoryClient>.Instance);

    private static string RepoJson(string name, string owner = "octo", bool isPrivate = false, string? branch = "dev") =>
        $$"""
        {"name":"{{name}}","full_name":"{{owner}}/{{name}}","html_url":"https://github.com/{{owner}}/{{name}}",
         "default_branch":{{(branch is null ? "null" : $"\"{branch}\"")}},"size":{{SizeKilobytes}},
         "private":{{(isPrivate ? "true" : "false")}},"owner":{"login":"{{owner}}"}
        }
        """;

    [Fact]
    public async Task ListRepositoriesAsync_ValidResponse_MapsRepositoriesAndSendsHeaders()
    {
        var handler = new RouteHandler(_ => RemoteResponses.Json($"[{RepoJson("hello", isPrivate: true)}]"));
        var client = CreateClient(handler);

        var repositories = await client.ListRepositoriesAsync(null, TestSupport.Ct);

        var repository = Assert.Single(repositories);
        Assert.Equal(
            new RemoteRepository(
                SourceType.Github,
                "octo",
                null,
                "hello",
                "octo/hello",
                "dev",
                "https://github.com/octo/hello",
                SizeKilobytes * BytesPerKilobyte,
                true),
            repository);
        var call = Assert.Single(handler.Calls);
        Assert.Equal(Origin + ReposPath, call.Uri.AbsoluteUri);
        Assert.Equal("application/vnd.github+json", call.Headers["Accept"]);
        Assert.Equal("2026-03-10", call.Headers["X-GitHub-Api-Version"]);
        Assert.Equal("CortexaCollector", call.Headers["User-Agent"]);
    }

    [Fact]
    public async Task ListRepositoriesAsync_UsesGitHubNamedClient()
    {
        var handler = new RouteHandler(_ => RemoteResponses.Json("[]"));
        var factory = new BaseAddressClientFactory(handler, $"{Origin}/");

        await CreateClient(handler, factory: factory).ListRepositoriesAsync(null, TestSupport.Ct);

        Assert.Equal(HttpClientNames.GitHub, factory.LastName);
    }

    [Fact]
    public async Task ListRepositoriesAsync_MissingDefaultBranch_FallsBackToMain()
    {
        var handler = new RouteHandler(_ => RemoteResponses.Json($"[{RepoJson("hello", branch: null)}]"));

        var repositories = await CreateClient(handler).ListRepositoriesAsync(null, TestSupport.Ct);

        Assert.Equal("main", Assert.Single(repositories).DefaultBranch);
    }

    [Fact]
    public async Task ListRepositoriesAsync_EntriesWithoutNameOrOwner_AreDropped()
    {
        const string Body = """[{"name":"x","owner":{}},{"owner":{"login":"octo"}}]""";
        var handler = new RouteHandler(_ => RemoteResponses.Json(Body));

        var repositories = await CreateClient(handler).ListRepositoriesAsync(null, TestSupport.Ct);

        Assert.Empty(repositories);
    }

    [Fact]
    public async Task ListRepositoriesAsync_LinkHeader_FollowsNextPage()
    {
        const string SecondPage = Origin + "/user/repos?page=2";
        var handler = new RouteHandler(call => call.Uri.AbsoluteUri == SecondPage
            ? RemoteResponses.Json($"[{RepoJson("second")}]")
            : RemoteResponses.Json($"[{RepoJson("first")}]", ("Link", $"<{SecondPage}>; rel=\"next\"")));

        var repositories = await CreateClient(handler).ListRepositoriesAsync(null, TestSupport.Ct);

        Assert.Equal(["first", "second"], repositories.Select(repository => repository.Name));
        Assert.Equal([Origin + ReposPath, SecondPage], handler.Calls.Select(call => call.Uri.AbsoluteUri));
    }

    [Fact]
    public async Task ListRepositoriesAsync_MaxPagesReached_StopsFollowingLinks()
    {
        const int MaxPages = 2;
        var handler = new RouteHandler(_ => RemoteResponses.Json(
            $"[{RepoJson("repo")}]",
            ("Link", $"<{Origin}/user/repos?page=9>; rel=\"next\"")));
        var client = CreateClient(handler, options => options.MaxPages = MaxPages);

        await client.ListRepositoriesAsync(null, TestSupport.Ct);

        Assert.Equal(MaxPages, handler.Calls.Count);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, RemoteFailureKind.Auth)]
    [InlineData(HttpStatusCode.NotFound, RemoteFailureKind.NotFound)]
    [InlineData(HttpStatusCode.InternalServerError, RemoteFailureKind.Upstream)]
    public async Task ListRepositoriesAsync_ErrorStatus_ThrowsMappedFailure(HttpStatusCode status, RemoteFailureKind expected)
    {
        var handler = new RouteHandler(_ => RemoteResponses.Status(status));

        var exception = await Assert.ThrowsAsync<RemoteSourceException>(
            () => CreateClient(handler).ListRepositoriesAsync(null, TestSupport.Ct));

        Assert.Equal(expected, exception.Kind);
    }

    [Fact]
    public async Task ListRepositoriesAsync_InvalidJson_ThrowsUpstream()
    {
        var handler = new RouteHandler(_ => RemoteResponses.Json("not json"));

        var exception = await Assert.ThrowsAsync<RemoteSourceException>(
            () => CreateClient(handler).ListRepositoriesAsync(null, TestSupport.Ct));

        Assert.Equal(RemoteFailureKind.Upstream, exception.Kind);
    }

    [Fact]
    public async Task ListRepositoriesAsync_WithOrganization_CallsTheOrgEndpointOnly()
    {
        var handler = new RouteHandler(_ => RemoteResponses.Json($"[{RepoJson("hello")}]"));

        var repositories = await CreateClient(handler).ListRepositoriesAsync("octo", TestSupport.Ct);

        Assert.Single(repositories);
        Assert.Equal($"{Origin}/orgs/octo/repos?type=all&per_page=100", Assert.Single(handler.Calls).Uri.AbsoluteUri);
    }

    [Fact]
    public async Task ListRepositoriesAsync_OrganizationNotFound_FallsBackToTheUserEndpoint()
    {
        var handler = new RouteHandler(call => call.Uri.AbsolutePath.StartsWith("/orgs/", StringComparison.Ordinal)
            ? RemoteResponses.Status(HttpStatusCode.NotFound)
            : RemoteResponses.Json($"[{RepoJson("mine")}]"));

        var repositories = await CreateClient(handler).ListRepositoriesAsync("someuser", TestSupport.Ct);

        Assert.Equal("mine", Assert.Single(repositories).Name);
        Assert.Equal(
            [$"{Origin}/orgs/someuser/repos?type=all&per_page=100", $"{Origin}/users/someuser/repos?type=owner&per_page=100"],
            handler.Calls.Select(call => call.Uri.AbsoluteUri));
    }

    [Fact]
    public async Task ListRepositoriesAsync_OrganizationAndUserNotFound_ThrowsNotFound()
    {
        var handler = new RouteHandler(_ => RemoteResponses.Status(HttpStatusCode.NotFound));

        var exception = await Assert.ThrowsAsync<RemoteSourceException>(
            () => CreateClient(handler).ListRepositoriesAsync("ghost", TestSupport.Ct));

        Assert.Equal(RemoteFailureKind.NotFound, exception.Kind);
        Assert.Equal(2, handler.Calls.Count);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, RemoteFailureKind.Auth)]
    [InlineData(HttpStatusCode.Forbidden, RemoteFailureKind.AccessDenied)]
    [InlineData(HttpStatusCode.InternalServerError, RemoteFailureKind.Upstream)]
    public async Task ListRepositoriesAsync_OrganizationErrorOtherThanNotFound_DoesNotFallBack(
        HttpStatusCode status,
        RemoteFailureKind expected)
    {
        var handler = new RouteHandler(_ => RemoteResponses.Status(status));

        var exception = await Assert.ThrowsAsync<RemoteSourceException>(
            () => CreateClient(handler).ListRepositoriesAsync("octo", TestSupport.Ct));

        Assert.Equal(expected, exception.Kind);
        Assert.Single(handler.Calls);
    }

    [Fact]
    public async Task ListRepositoriesAsync_RateLimited_ReachesTheCallerWithoutFallback()
    {
        var handler = new RouteHandler(_ => RemoteResponses.Status(HttpStatusCode.TooManyRequests, ("Retry-After", "30")));

        var exception = await Assert.ThrowsAsync<RemoteSourceException>(
            () => CreateClient(handler).ListRepositoriesAsync("octo", TestSupport.Ct));

        Assert.Equal(RemoteFailureKind.RateLimited, exception.Kind);
        Assert.Single(handler.Calls);
    }

    [Fact]
    public async Task ListRepositoriesAsync_OrganizationPages_FollowTheLinkHeader()
    {
        var handler = new RouteHandler(call => call.Uri.Query.Contains("page=2", StringComparison.Ordinal)
            ? RemoteResponses.Json($"[{RepoJson("two")}]")
            : RemoteResponses.Json(
                $"[{RepoJson("one")}]",
                ("Link", $"<{Origin}/orgs/octo/repos?type=all&per_page=100&page=2>; rel=\"next\"")));

        var repositories = await CreateClient(handler).ListRepositoriesAsync("octo", TestSupport.Ct);

        Assert.Equal(["one", "two"], repositories.Select(repository => repository.Name));
    }

    [Fact]
    public async Task ListRepositoriesAsync_MapsDescriptionAndUpdatedTime()
    {
        const string Body = """
            [{"name":"hello","full_name":"octo/hello","default_branch":"main","size":1,"private":false,
              "description":"Billing service","updated_at":"2026-09-30T10:15:00Z","owner":{"login":"octo"}}]
            """;
        var handler = new RouteHandler(_ => RemoteResponses.Json(Body));

        var repository = Assert.Single(await CreateClient(handler).ListRepositoriesAsync("octo", TestSupport.Ct));

        Assert.Equal("Billing service", repository.Description);
        Assert.Equal(new DateTimeOffset(2026, 9, 30, 10, 15, 0, TimeSpan.Zero), repository.UpdatedAt);
    }

    [Fact]
    public async Task ListRepositoriesAsync_MissingDescriptionAndDate_AreNull()
    {
        var handler = new RouteHandler(_ => RemoteResponses.Json($"[{RepoJson("hello")}]"));

        var repository = Assert.Single(await CreateClient(handler).ListRepositoriesAsync("octo", TestSupport.Ct));

        Assert.Null(repository.Description);
        Assert.Null(repository.UpdatedAt);
    }

    [Fact]
    public async Task ListBranchesAsync_MapsTheProtectedFlag()
    {
        const string Body = """[{"name":"main","commit":{"sha":"a1"},"protected":true},{"name":"dev","commit":{"sha":"b2"}}]""";
        var handler = new RouteHandler(_ => RemoteResponses.Json(Body));

        var branches = await CreateClient(handler).ListBranchesAsync(RemoteData.GitHubRepo(), TestSupport.Ct);

        Assert.Equal([new RemoteBranch("main", "a1", true), new RemoteBranch("dev", "b2")], branches);
    }

    [Fact]
    public async Task ListBranchesAsync_ValidResponse_MapsNamesAndCommits()
    {
        const string Body = """[{"name":"main","commit":{"sha":"a1"}},{"name":"broken"},{"commit":{"sha":"b2"}}]""";
        var handler = new RouteHandler(_ => RemoteResponses.Json(Body));

        var branches = await CreateClient(handler).ListBranchesAsync(RemoteData.GitHubRepo(), TestSupport.Ct);

        Assert.Equal([new RemoteBranch("main", "a1")], branches);
        Assert.Equal($"{RepoRoot}/branches?per_page=100", Assert.Single(handler.Calls).Uri.AbsoluteUri);
    }

    [Fact]
    public async Task GetTreeAsync_ValidResponse_ReadsRefThenRecursiveTreeAndKeepsBlobsOnly()
    {
        const string TreeBody = $$"""
            {"sha":"{{TreeSha}}","truncated":true,"tree":[
              {"path":"README.md","type":"blob","sha":"b1","size":12},
              {"path":"docs","type":"tree","sha":"t1"},
              {"path":"vendor","type":"commit","sha":"c1"},
              {"path":"","type":"blob","sha":"b2"}]}
            """;
        var handler = new RouteHandler(call => call.Uri.AbsolutePath.Contains("/git/ref/", StringComparison.Ordinal)
            ? RemoteResponses.Json($$$"""{"object":{"sha":"{{{CommitSha}}}"}}""")
            : RemoteResponses.Json(TreeBody));

        var tree = await CreateClient(handler).GetTreeAsync(RemoteData.GitHubRepo(), "feature/x y", TestSupport.Ct);

        Assert.Equal(CommitSha, tree.CommitSha);
        Assert.True(tree.Truncated);
        Assert.Equal([new RemoteTreeEntry("README.md", "b1", 12)], tree.Entries);
        Assert.Equal(
            [$"{RepoRoot}/git/ref/heads/feature/x%20y", $"{RepoRoot}/git/trees/{CommitSha}?recursive=1"],
            handler.Calls.Select(call => call.Uri.AbsoluteUri));
    }

    [Fact]
    public async Task GetTreeAsync_RefWithoutObject_ThrowsUpstream()
    {
        var handler = new RouteHandler(_ => RemoteResponses.Json("{}"));

        var exception = await Assert.ThrowsAsync<RemoteSourceException>(
            () => CreateClient(handler).GetTreeAsync(RemoteData.GitHubRepo(), "main", TestSupport.Ct));

        Assert.Equal(RemoteFailureKind.Upstream, exception.Kind);
    }

    [Fact]
    public async Task GetTreeAsync_ConflictOnEmptyRepository_ThrowsEmptyRepository()
    {
        var handler = new RouteHandler(_ => RemoteResponses.Status(HttpStatusCode.Conflict));

        var exception = await Assert.ThrowsAsync<RemoteSourceException>(
            () => CreateClient(handler).GetTreeAsync(RemoteData.GitHubRepo(), "main", TestSupport.Ct));

        Assert.Equal(RemoteFailureKind.EmptyRepository, exception.Kind);
    }

    [Fact]
    public async Task OpenBlobAsync_ValidBlob_StreamsRawContentWithRawAcceptHeader()
    {
        const string Content = "# hello";
        var handler = new RouteHandler(_ => RemoteResponses.Text(Content));

        using var blob = await CreateClient(handler).OpenBlobAsync(RemoteData.GitHubRepo(), "b1", TestSupport.Ct);
        using var reader = new StreamReader(blob.Content);

        Assert.Equal(Content, await reader.ReadToEndAsync(TestSupport.Ct));
        Assert.Equal(Content.Length, blob.Length);
        var call = Assert.Single(handler.Calls);
        Assert.Equal($"{RepoRoot}/git/blobs/b1", call.Uri.AbsoluteUri);
        Assert.Equal("application/vnd.github.raw+json", call.Headers["Accept"]);
    }

    [Fact]
    public async Task OpenBlobAsync_NotFound_ThrowsNotFound()
    {
        var handler = new RouteHandler(_ => RemoteResponses.Status(HttpStatusCode.NotFound));

        var exception = await Assert.ThrowsAsync<RemoteSourceException>(
            () => CreateClient(handler).OpenBlobAsync(RemoteData.GitHubRepo(), "b1", TestSupport.Ct));

        Assert.Equal(RemoteFailureKind.NotFound, exception.Kind);
    }
}
