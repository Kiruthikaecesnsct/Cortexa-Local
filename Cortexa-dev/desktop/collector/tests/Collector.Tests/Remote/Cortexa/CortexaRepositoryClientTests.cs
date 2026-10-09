using System.Net;
using Collector.Application.Remote;
using Collector.Domain.Enums;
using Collector.Domain.Remote;
using Collector.Infrastructure.Remote.Cortexa;
using Collector.Tests.Support;

namespace Collector.Tests.Remote.Cortexa;

public class CortexaRepositoryClientTests
{
    private const string Sha = "sha1";
    private const long CloneSizeBytes = 4096;
    private const int MaxOwner = 64;
    private const int MaxRepository = 129;
    private const int MaxBranch = 255;

    private static HttpResponseMessage ByPrefix(RecordedCall call, HttpResponseMessage github, HttpResponseMessage azure) =>
        call.Uri.AbsolutePath switch
        {
            CortexaData.GitHubClonesPath => github,
            CortexaData.AzureClonesPath => azure,
            _ => RemoteResponses.Status(HttpStatusCode.NotFound),
        };

    private static CortexaFixture Listing(HttpResponseMessage github, HttpResponseMessage azure) =>
        new(call => ByPrefix(call, github, azure));

    private static CortexaFixture ServingTree(string[] files, params object[] clones) => new(call =>
        call.Uri.AbsolutePath.EndsWith("/files", StringComparison.Ordinal)
            ? CortexaData.FileList(files)
            : CortexaData.CloneList(clones));

    [Fact]
    public async Task ListRepositoriesAsync_BothPrefixes_MergesAndTagsEachRepository()
    {
        using var fixture = Listing(
            CortexaData.CloneList(CortexaData.Clone(repository: "alpha")),
            CortexaData.CloneList(CortexaData.Clone(owner: "contoso", repository: "beta")));

        var repositories = await fixture.Client.ListRepositoriesAsync(null, TestSupport.Ct);

        Assert.Equal(
            [(CortexaData.GitHubTag, "octo/alpha"), (CortexaData.AzureTag, "contoso/beta")],
            repositories.Select(repository => (repository.Project!, repository.FullName)).OrderByDescending(pair => pair.Item1));
    }

    [Fact]
    public async Task ListRepositoriesAsync_MixedStatuses_KeepsOnlyStoredClones()
    {
        using var fixture = Listing(
            CortexaData.CloneList(
                CortexaData.Clone(repository: "stored"),
                CortexaData.Clone(repository: "pending", status: "pending"),
                CortexaData.Clone(repository: "failed", status: "failed")),
            CortexaData.CloneList());

        var repositories = await fixture.Client.ListRepositoriesAsync(null, TestSupport.Ct);

        Assert.Equal("stored", Assert.Single(repositories).Name);
    }

    [Fact]
    public async Task ListRepositoriesAsync_MapsCloneToRepository()
    {
        using var fixture = Listing(
            CortexaData.CloneList(CortexaData.Clone(branch: "develop", sizeBytes: CloneSizeBytes)),
            CortexaData.CloneList());

        var repository = Assert.Single(await fixture.Client.ListRepositoriesAsync(null, TestSupport.Ct));

        Assert.Equal(
            new RemoteRepository(
                SourceType.CortexaRepo,
                "octo",
                CortexaData.GitHubTag,
                "hello",
                "octo/hello",
                "develop",
                "cortexa://github/octo/hello",
                CloneSizeBytes,
                false),
            repository);
    }

    [Fact]
    public async Task ListRepositoriesAsync_SameRepositoryOnBothPrefixes_ProducesDistinctRepoKeys()
    {
        using var fixture = Listing(
            CortexaData.CloneList(CortexaData.Clone()),
            CortexaData.CloneList(CortexaData.Clone()));

        var repositories = await fixture.Client.ListRepositoriesAsync(null, TestSupport.Ct);

        Assert.Equal(2, repositories.Select(repository => repository.RepoKey).Distinct().Count());
    }

    [Fact]
    public async Task ListRepositoriesAsync_CloneMissingOwnerRepositoryOrBranch_IsDropped()
    {
        using var fixture = Listing(
            CortexaData.CloneList(
                CortexaData.Clone(owner: string.Empty),
                CortexaData.Clone(repository: string.Empty),
                CortexaData.Clone(branch: string.Empty),
                CortexaData.Clone(repository: "good")),
            CortexaData.CloneList());

        var repositories = await fixture.Client.ListRepositoriesAsync(null, TestSupport.Ct);

        Assert.Equal("good", Assert.Single(repositories).Name);
    }

    [Fact]
    public async Task ListRepositoriesAsync_OnePrefixNotFound_StillReturnsTheOther()
    {
        using var fixture = Listing(
            RemoteResponses.Status(HttpStatusCode.NotFound),
            CortexaData.CloneList(CortexaData.Clone(repository: "beta")));

        var repositories = await fixture.Client.ListRepositoriesAsync(null, TestSupport.Ct);

        Assert.Equal("beta", Assert.Single(repositories).Name);
    }

    [Fact]
    public async Task ListRepositoriesAsync_BothPrefixesNotFound_ReturnsEmpty()
    {
        using var fixture = Listing(
            RemoteResponses.Status(HttpStatusCode.NotFound),
            RemoteResponses.Status(HttpStatusCode.NotFound));

        var repositories = await fixture.Client.ListRepositoriesAsync(null, TestSupport.Ct);

        Assert.Empty(repositories);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, RemoteFailureKind.Auth, true)]
    [InlineData(HttpStatusCode.Unauthorized, RemoteFailureKind.Auth, false)]
    [InlineData(HttpStatusCode.Forbidden, RemoteFailureKind.AccessDenied, true)]
    [InlineData(HttpStatusCode.Forbidden, RemoteFailureKind.AccessDenied, false)]
    [InlineData(HttpStatusCode.InternalServerError, RemoteFailureKind.Upstream, true)]
    public async Task ListRepositoriesAsync_AuthOrServerErrorOnEitherPrefix_FailsTheWholeList(
        HttpStatusCode status,
        RemoteFailureKind expected,
        bool failGitHub)
    {
        var failing = RemoteResponses.Status(status);
        var healthy = CortexaData.CloneList(CortexaData.Clone());
        using var fixture = failGitHub ? Listing(failing, healthy) : Listing(healthy, failing);

        var exception = await Assert.ThrowsAsync<RemoteSourceException>(
            () => fixture.Client.ListRepositoriesAsync(null, TestSupport.Ct));

        Assert.Equal(expected, exception.Kind);
        Assert.Equal(SourceType.CortexaRepo, exception.Provider);
    }

    [Fact]
    public async Task ListRepositoriesAsync_ResponseWithoutData_ThrowsUpstream()
    {
        using var fixture = Listing(RemoteResponses.Json("{\"success\":true}"), CortexaData.CloneList());

        var exception = await Assert.ThrowsAsync<RemoteSourceException>(
            () => fixture.Client.ListRepositoriesAsync(null, TestSupport.Ct));

        Assert.Equal(RemoteFailureKind.Upstream, exception.Kind);
    }

    [Fact]
    public async Task ListRepositoriesAsync_MalformedJson_ThrowsUpstream()
    {
        using var fixture = Listing(RemoteResponses.Json("not json"), CortexaData.CloneList());

        var exception = await Assert.ThrowsAsync<RemoteSourceException>(
            () => fixture.Client.ListRepositoriesAsync(null, TestSupport.Ct));

        Assert.Equal(RemoteFailureKind.Upstream, exception.Kind);
    }

    [Fact]
    public async Task ListRepositoriesAsync_RequestsJsonFromBothClonePaths()
    {
        using var fixture = Listing(CortexaData.CloneList(), CortexaData.CloneList());

        await fixture.Client.ListRepositoriesAsync(null, TestSupport.Ct);

        Assert.Equal(
            [CortexaData.AzureClonesPath, CortexaData.GitHubClonesPath],
            fixture.Handler.Calls.Select(call => call.Uri.AbsolutePath).Order());
        Assert.All(fixture.Handler.Calls, call => Assert.Equal("application/json", call.Headers["Accept"]));
    }

    [Fact]
    public async Task ListBranchesAsync_MatchingClones_ReturnsEachSavedBranchWithItsCommit()
    {
        using var fixture = Listing(
            CortexaData.CloneList(
                CortexaData.Clone(branch: "main", commitSha: "aaa"),
                CortexaData.Clone(branch: "dev", commitSha: "bbb"),
                CortexaData.Clone(repository: "other", branch: "x")),
            CortexaData.CloneList());

        var branches = await fixture.Client.ListBranchesAsync(CortexaData.Repo(), TestSupport.Ct);

        Assert.Equal([new RemoteBranch("main", "aaa"), new RemoteBranch("dev", "bbb")], branches);
    }

    [Fact]
    public async Task ListBranchesAsync_NoMatchingClone_ThrowsNotFound()
    {
        using var fixture = Listing(CortexaData.CloneList(CortexaData.Clone(repository: "other")), CortexaData.CloneList());

        var exception = await Assert.ThrowsAsync<RemoteSourceException>(
            () => fixture.Client.ListBranchesAsync(CortexaData.Repo(), TestSupport.Ct));

        Assert.Equal(RemoteFailureKind.NotFound, exception.Kind);
    }

    [Fact]
    public async Task GetTreeAsync_ServedFiles_BuildsEntriesWithKeysThatCarryBranchVersionAndPath()
    {
        using var fixture = ServingTree(["README.md", "docs/a.md"], CortexaData.Clone());

        var tree = await fixture.Client.GetTreeAsync(CortexaData.Repo(), "main", TestSupport.Ct);

        Assert.Equal(Sha, tree.CommitSha);
        Assert.False(tree.Truncated);
        Assert.Equal(["README.md", "docs/a.md"], tree.Entries.Select(entry => entry.Path));
        Assert.Equal(
            [new CortexaBlobKey("main", Sha, "README.md"), new CortexaBlobKey("main", Sha, "docs/a.md")],
            tree.Entries.Select(entry => CortexaBlobKeyCodec.Parse(entry.BlobSha)));
    }

    [Fact]
    public async Task GetTreeAsync_UnsafeAndDuplicatePaths_AreDropped()
    {
        string[] files =
        [
            "ok.md",
            "../x",
            "/abs",
            "a\\b",
            "C:x",
            "CON.md",
            "bad\u0001.md",
            "bad\u0002.md",
            "docs/../../y",
            "ok.md",
        ];
        using var fixture = ServingTree(files, CortexaData.Clone());

        var tree = await fixture.Client.GetTreeAsync(CortexaData.Repo(), "main", TestSupport.Ct);

        Assert.Equal(["ok.md"], tree.Entries.Select(entry => entry.Path));
    }

    [Fact]
    public async Task GetTreeAsync_CloneWithoutCommitSha_UsesUpdatedAtAsTheVersion()
    {
        using var fixture = ServingTree(["a.md"], CortexaData.Clone(commitSha: null));

        var tree = await fixture.Client.GetTreeAsync(CortexaData.Repo(), "main", TestSupport.Ct);

        Assert.Equal("2026-10-02T00:00:00Z", tree.CommitSha);
    }

    [Fact]
    public async Task GetTreeAsync_PicksTheCloneForTheRequestedBranch()
    {
        using var fixture = ServingTree(
            ["a.md"],
            CortexaData.Clone(branch: "main", commitSha: "aaa"),
            CortexaData.Clone(branch: "dev", commitSha: "bbb"));

        var tree = await fixture.Client.GetTreeAsync(CortexaData.Repo(), "dev", TestSupport.Ct);

        Assert.Equal("bbb", tree.CommitSha);
    }

    [Theory]
    [InlineData("dev")]
    [InlineData("MAIN")]
    public async Task GetTreeAsync_NoCloneForBranch_ThrowsNotFoundWithoutAskingForFiles(string branch)
    {
        using var fixture = ServingTree(["a.md"], CortexaData.Clone(branch: "main"));

        var exception = await Assert.ThrowsAsync<RemoteSourceException>(
            () => fixture.Client.GetTreeAsync(CortexaData.Repo(), branch, TestSupport.Ct));

        Assert.Equal(RemoteFailureKind.NotFound, exception.Kind);
        Assert.DoesNotContain(fixture.Handler.Calls, call => call.Uri.AbsolutePath.EndsWith("/files", StringComparison.Ordinal));
    }

    [Fact]
    public async Task GetTreeAsync_CloneNotStored_ThrowsNotFound()
    {
        using var fixture = ServingTree(["a.md"], CortexaData.Clone(status: "pending"));

        var exception = await Assert.ThrowsAsync<RemoteSourceException>(
            () => fixture.Client.GetTreeAsync(CortexaData.Repo(), "main", TestSupport.Ct));

        Assert.Equal(RemoteFailureKind.NotFound, exception.Kind);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("gitlab")]
    public async Task GetTreeAsync_UnknownUpstreamTag_ThrowsNotFoundWithoutCallingTheGateway(string? tag)
    {
        using var fixture = ServingTree(["a.md"], CortexaData.Clone());
        var repository = CortexaData.Repo() with { Project = tag };

        var exception = await Assert.ThrowsAsync<RemoteSourceException>(
            () => fixture.Client.GetTreeAsync(repository, "main", TestSupport.Ct));

        Assert.Equal(RemoteFailureKind.NotFound, exception.Kind);
        Assert.Empty(fixture.Handler.Calls);
    }

    [Fact]
    public async Task GetTreeAsync_NamesWithSpecialCharacters_AreEscapedInTheFilesQuery()
    {
        const string Name = "my repo&x=1";
        const string Branch = "feature/a b";
        using var fixture = ServingTree(["a.md"], CortexaData.Clone(repository: Name, branch: Branch));

        await fixture.Client.GetTreeAsync(CortexaData.Repo(name: Name), Branch, TestSupport.Ct);

        var files = fixture.Handler.Calls.Single(call => call.Uri.AbsolutePath == "/scan/github/clones/files");
        Assert.Equal("?owner=octo&repository=my%20repo%26x%3D1&branch=feature%2Fa%20b", files.Uri.Query);
    }

    [Theory]
    [InlineData(MaxOwner + 1, 5, 4)]
    [InlineData(5, MaxRepository + 1, 4)]
    [InlineData(5, 5, MaxBranch + 1)]
    public async Task GetTreeAsync_NameOverLengthLimit_ThrowsNotFoundWithoutAskingForFiles(
        int ownerLength,
        int repositoryLength,
        int branchLength)
    {
        var owner = new string('o', ownerLength);
        var name = new string('r', repositoryLength);
        var branch = new string('b', branchLength);
        using var fixture = ServingTree(["a.md"], CortexaData.Clone(owner: owner, repository: name, branch: branch));

        var exception = await Assert.ThrowsAsync<RemoteSourceException>(
            () => fixture.Client.GetTreeAsync(CortexaData.Repo(owner: owner, name: name), branch, TestSupport.Ct));

        Assert.Equal(RemoteFailureKind.NotFound, exception.Kind);
        Assert.DoesNotContain(fixture.Handler.Calls, call => call.Uri.AbsolutePath.EndsWith("/files", StringComparison.Ordinal));
    }

    [Fact]
    public async Task GetTreeAsync_NamesAtTheLengthLimits_AreAccepted()
    {
        var owner = new string('o', MaxOwner);
        var name = new string('r', MaxRepository);
        var branch = new string('b', MaxBranch);
        using var fixture = ServingTree(["a.md"], CortexaData.Clone(owner: owner, repository: name, branch: branch));

        var tree = await fixture.Client.GetTreeAsync(CortexaData.Repo(owner: owner, name: name), branch, TestSupport.Ct);

        Assert.Single(tree.Entries);
    }

    [Fact]
    public async Task GetTreeAsync_FilesResponseWithoutList_ReturnsEmptyTree()
    {
        using var fixture = new CortexaFixture(call =>
            call.Uri.AbsolutePath.EndsWith("/files", StringComparison.Ordinal)
                ? RemoteResponses.Json("{\"success\":true,\"data\":{}}")
                : CortexaData.CloneList(CortexaData.Clone()));

        var tree = await fixture.Client.GetTreeAsync(CortexaData.Repo(), "main", TestSupport.Ct);

        Assert.Empty(tree.Entries);
    }

    [Fact]
    public async Task OpenBlobAsync_KeyFromTree_ServesTheMatchingZipEntry()
    {
        const string Path = "docs/a.md";
        const string Text = "# from the zip";
        var zip = CortexaData.Zip((Path, CortexaData.Text(Text)), ("other.md", CortexaData.Text("other")));
        using var fixture = new CortexaFixture(_ => RemoteResponses.Bytes(zip));
        var key = CortexaBlobKeyCodec.Encode("main", Sha, Path);

        using var blob = await fixture.Client.OpenBlobAsync(CortexaData.Repo(), key, TestSupport.Ct);

        using var reader = new StreamReader(blob.Content);
        Assert.Equal(Text, await reader.ReadToEndAsync(TestSupport.Ct));
        var call = Assert.Single(fixture.Handler.Calls);
        Assert.Equal("/scan/github/clones/download", call.Uri.AbsolutePath);
        Assert.Equal("?owner=octo&repository=hello&branch=main", call.Uri.Query);
    }

    [Fact]
    public async Task OpenBlobAsync_ManyFilesOfOneBranch_DownloadTheArchiveOnce()
    {
        var zip = CortexaData.Zip(("a.md", CortexaData.Text("a")), ("b.md", CortexaData.Text("b")));
        using var fixture = new CortexaFixture(_ => RemoteResponses.Bytes(zip));

        using var first = await fixture.Client.OpenBlobAsync(CortexaData.Repo(), CortexaBlobKeyCodec.Encode("main", Sha, "a.md"), TestSupport.Ct);
        using var second = await fixture.Client.OpenBlobAsync(CortexaData.Repo(), CortexaBlobKeyCodec.Encode("main", Sha, "b.md"), TestSupport.Ct);

        Assert.Single(fixture.Handler.Calls);
    }

    [Fact]
    public async Task OpenBlobAsync_MalformedKey_ThrowsUpstream()
    {
        using var fixture = new CortexaFixture(_ => RemoteResponses.Status(HttpStatusCode.InternalServerError));

        var exception = await Assert.ThrowsAsync<RemoteSourceException>(
            () => fixture.Client.OpenBlobAsync(CortexaData.Repo(), "not-a-cortexa-key", TestSupport.Ct));

        Assert.Equal(RemoteFailureKind.Upstream, exception.Kind);
        Assert.Empty(fixture.Handler.Calls);
    }

    [Fact]
    public async Task OpenBlobAsync_PathMissingFromArchive_ThrowsNotFound()
    {
        var zip = CortexaData.Zip(("a.md", CortexaData.Text("a")));
        using var fixture = new CortexaFixture(_ => RemoteResponses.Bytes(zip));

        var exception = await Assert.ThrowsAsync<RemoteSourceException>(
            () => fixture.Client.OpenBlobAsync(CortexaData.Repo(), CortexaBlobKeyCodec.Encode("main", Sha, "gone.md"), TestSupport.Ct));

        Assert.Equal(RemoteFailureKind.NotFound, exception.Kind);
    }

    [Fact]
    public async Task CompleteFetchAsync_AfterDownloads_DeletesTheBranchArchive()
    {
        var zip = CortexaData.Zip(("a.md", CortexaData.Text("a")));
        using var fixture = new CortexaFixture(_ => RemoteResponses.Bytes(zip));
        using (await fixture.Client.OpenBlobAsync(CortexaData.Repo(), CortexaBlobKeyCodec.Encode("main", Sha, "a.md"), TestSupport.Ct))
        {
        }

        await fixture.Client.CompleteFetchAsync(CortexaData.Repo(), "main");

        Assert.Empty(fixture.ArchiveFiles);
    }

    [Fact]
    public async Task CompleteFetchAsync_OtherBranchDownloaded_KeepsThatArchive()
    {
        var zip = CortexaData.Zip(("a.md", CortexaData.Text("a")));
        using var fixture = new CortexaFixture(_ => RemoteResponses.Bytes(zip));
        using (await fixture.Client.OpenBlobAsync(CortexaData.Repo(), CortexaBlobKeyCodec.Encode("dev", Sha, "a.md"), TestSupport.Ct))
        {
        }

        await fixture.Client.CompleteFetchAsync(CortexaData.Repo(), "main");

        Assert.Single(fixture.ArchiveFiles);
    }

    [Fact]
    public async Task CompleteFetchAsync_RepositoryWithoutTag_CompletesWithoutError()
    {
        using var fixture = new CortexaFixture(_ => RemoteResponses.Status(HttpStatusCode.InternalServerError));

        await fixture.Client.CompleteFetchAsync(CortexaData.Repo() with { Project = null }, "main");

        Assert.Empty(fixture.Handler.Calls);
    }

    [Fact]
    public void Provider_Always_IsCortexaRepo()
    {
        using var fixture = new CortexaFixture(_ => RemoteResponses.Status(HttpStatusCode.OK));

        Assert.Equal(SourceType.CortexaRepo, fixture.Client.Provider);
    }
}
