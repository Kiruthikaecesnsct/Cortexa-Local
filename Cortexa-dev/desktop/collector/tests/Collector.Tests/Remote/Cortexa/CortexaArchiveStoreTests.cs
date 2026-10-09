using System.Net;
using Collector.Application.Extraction;
using Collector.Application.Remote;
using Collector.Domain.Enums;
using Collector.Infrastructure.Remote.Cortexa;
using Collector.Tests.Support;

namespace Collector.Tests.Remote.Cortexa;

public class CortexaArchiveStoreTests
{
    private const string Version = "sha1";
    private const string ServedPath = "docs/a.md";
    private const string ServedText = "# hello";
    private const int ParallelCallers = 4;
    private const long TinyLimit = 10;
    private const int OversizedArchiveBytes = 64;
    private const int OverLimitOwnerLength = 65;

    private static readonly CortexaArchiveRequest Request = new("github", "octo", "hello", "main", Version);

    private static byte[] DefaultZip() => CortexaData.Zip((ServedPath, CortexaData.Text(ServedText)));

    private static CortexaFixture ServingZip(byte[]? zip = null, long maxRepositoryBytes = CortexaFixture.DefaultMaxRepositoryBytes)
    {
        var bytes = zip ?? DefaultZip();
        return new CortexaFixture(_ => RemoteResponses.Bytes(bytes), maxRepositoryBytes);
    }

    private static async Task<string> ReadAllAsync(RemoteBlob blob)
    {
        using var reader = new StreamReader(blob.Content);
        return await reader.ReadToEndAsync(TestSupport.Ct);
    }

    [Theory]
    [InlineData("../x")]
    [InlineData("/abs")]
    [InlineData("a\\b")]
    [InlineData("C:x")]
    [InlineData("CON.md")]
    [InlineData("docs/../../x")]
    public async Task OpenEntryAsync_UnsafeEntryName_IsNeverServed(string name)
    {
        var zip = CortexaData.Zip((name, CortexaData.Text("secret")), (ServedPath, CortexaData.Text(ServedText)));
        using var fixture = ServingZip(zip);
        var path = await fixture.Store.AcquireAsync(Request, TestSupport.Ct);

        var exception = await Assert.ThrowsAsync<RemoteSourceException>(
            () => fixture.Store.OpenEntryAsync(path, name, TestSupport.Ct));

        Assert.Equal(RemoteFailureKind.NotFound, exception.Kind);
        Assert.Equal(SourceType.CortexaRepo, exception.Provider);
    }

    [Fact]
    public async Task OpenEntryAsync_KnownPath_ServesEntryContent()
    {
        using var fixture = ServingZip();
        var path = await fixture.Store.AcquireAsync(Request, TestSupport.Ct);

        using var blob = await fixture.Store.OpenEntryAsync(path, ServedPath, TestSupport.Ct);

        Assert.Equal(ServedText, await ReadAllAsync(blob));
        Assert.Equal(ServedText.Length, blob.Length);
    }

    [Theory]
    [InlineData("docs/missing.md")]
    [InlineData("DOCS/A.MD")]
    public async Task OpenEntryAsync_PathNotInArchive_ThrowsNotFound(string relativePath)
    {
        using var fixture = ServingZip();
        var path = await fixture.Store.AcquireAsync(Request, TestSupport.Ct);

        var exception = await Assert.ThrowsAsync<RemoteSourceException>(
            () => fixture.Store.OpenEntryAsync(path, relativePath, TestSupport.Ct));

        Assert.Equal(RemoteFailureKind.NotFound, exception.Kind);
    }

    [Fact]
    public async Task OpenEntryAsync_NotAZipFile_ThrowsUpstream()
    {
        using var fixture = ServingZip(CortexaData.Text("this is not a zip archive"));
        var path = await fixture.Store.AcquireAsync(Request, TestSupport.Ct);

        var exception = await Assert.ThrowsAsync<RemoteSourceException>(
            () => fixture.Store.OpenEntryAsync(path, ServedPath, TestSupport.Ct));

        Assert.Equal(RemoteFailureKind.Upstream, exception.Kind);
    }

    [Fact]
    public async Task OpenEntryAsync_EntryLargerThanFileLimit_ReportsItsRealLength()
    {
        const string BigPath = "big.md";
        var oversized = new byte[FileContentGuard.MaxFileBytes + 1];
        using var fixture = ServingZip(CortexaData.Zip((BigPath, oversized)));
        var path = await fixture.Store.AcquireAsync(Request, TestSupport.Ct);

        using var blob = await fixture.Store.OpenEntryAsync(path, BigPath, TestSupport.Ct);

        Assert.Equal(FileContentGuard.MaxFileBytes + 1, blob.Length);
    }

    [Fact]
    public async Task AcquireAsync_ContentLengthOverLimit_ThrowsRepositoryTooLargeAndLeavesNoFiles()
    {
        using var fixture = new CortexaFixture(_ => RemoteResponses.Bytes(new byte[OversizedArchiveBytes]), TinyLimit);

        var exception = await Assert.ThrowsAsync<RemoteSourceException>(
            () => fixture.Store.AcquireAsync(Request, TestSupport.Ct));

        Assert.Equal(RemoteFailureKind.RepositoryTooLarge, exception.Kind);
        Assert.Empty(fixture.ArchiveFiles);
    }

    [Fact]
    public async Task AcquireAsync_NoContentLengthAndBodyOverLimit_ThrowsRepositoryTooLargeAndRemovesTempFile()
    {
        using var fixture = new CortexaFixture(
            _ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new UnknownLengthContent(new byte[OversizedArchiveBytes]) },
            TinyLimit);

        var exception = await Assert.ThrowsAsync<RemoteSourceException>(
            () => fixture.Store.AcquireAsync(Request, TestSupport.Ct));

        Assert.Equal(RemoteFailureKind.RepositoryTooLarge, exception.Kind);
        Assert.Empty(fixture.ArchiveFiles);
    }

    [Fact]
    public async Task AcquireAsync_ArchiveExactlyAtLimit_IsAccepted()
    {
        var zip = DefaultZip();
        using var fixture = ServingZip(zip, zip.Length);

        var path = await fixture.Store.AcquireAsync(Request, TestSupport.Ct);

        Assert.True(File.Exists(path));
    }

    [Fact]
    public async Task AcquireAsync_ParallelCallersForSameKey_DownloadOnce()
    {
        using var fixture = ServingZip();

        var paths = await Task.WhenAll(Enumerable.Range(0, ParallelCallers)
            .Select(_ => Task.Run(() => fixture.Store.AcquireAsync(Request, TestSupport.Ct), TestSupport.Ct)));

        Assert.Single(fixture.Handler.Calls);
        Assert.Single(paths.Distinct());
    }

    [Fact]
    public async Task AcquireAsync_DifferentVersions_DownloadSeparately()
    {
        using var fixture = ServingZip();

        await fixture.Store.AcquireAsync(Request, TestSupport.Ct);
        await fixture.Store.AcquireAsync(Request with { Version = "sha2" }, TestSupport.Ct);

        Assert.Equal(2, fixture.Handler.Calls.Count);
    }

    [Fact]
    public async Task AcquireAsync_AfterFailure_RetriesWithAFreshDownload()
    {
        var bytes = DefaultZip();
        var attempt = 0;
        using var fixture = new CortexaFixture(_ =>
            Interlocked.Increment(ref attempt) == 1
                ? RemoteResponses.Status(HttpStatusCode.InternalServerError)
                : RemoteResponses.Bytes(bytes));
        var first = await Assert.ThrowsAsync<RemoteSourceException>(
            () => fixture.Store.AcquireAsync(Request, TestSupport.Ct));

        var path = await fixture.Store.AcquireAsync(Request, TestSupport.Ct);

        Assert.Equal(RemoteFailureKind.Upstream, first.Kind);
        Assert.True(File.Exists(path));
        Assert.Equal(2, fixture.Handler.Calls.Count);
    }

    [Fact]
    public async Task AcquireAsync_SendsZipAcceptHeaderToTheDownloadEndpoint()
    {
        using var fixture = ServingZip();

        await fixture.Store.AcquireAsync(Request with { Owner = "my org", Repository = "a&b" }, TestSupport.Ct);

        var call = Assert.Single(fixture.Handler.Calls);
        Assert.Equal("application/zip", call.Headers["Accept"]);
        Assert.Equal("/scan/github/clones/download", call.Uri.AbsolutePath);
        Assert.Equal("?owner=my%20org&repository=a%26b&branch=main", call.Uri.Query);
    }

    [Theory]
    [InlineData("unknown-tag", "octo", "hello", "main")]
    [InlineData("github", "", "hello", "main")]
    [InlineData("github", "octo", "hello", "")]
    public async Task AcquireAsync_UnroutableRequest_ThrowsNotFoundWithoutCallingTheGateway(
        string tag,
        string owner,
        string repository,
        string branch)
    {
        using var fixture = ServingZip();

        var exception = await Assert.ThrowsAsync<RemoteSourceException>(
            () => fixture.Store.AcquireAsync(new CortexaArchiveRequest(tag, owner, repository, branch, Version), TestSupport.Ct));

        Assert.Equal(RemoteFailureKind.NotFound, exception.Kind);
        Assert.Empty(fixture.Handler.Calls);
    }

    [Fact]
    public async Task AcquireAsync_OwnerOverLengthLimit_ThrowsNotFoundWithoutCallingTheGateway()
    {
        using var fixture = ServingZip();
        var request = Request with { Owner = new string('o', OverLimitOwnerLength) };

        var exception = await Assert.ThrowsAsync<RemoteSourceException>(
            () => fixture.Store.AcquireAsync(request, TestSupport.Ct));

        Assert.Equal(RemoteFailureKind.NotFound, exception.Kind);
        Assert.Empty(fixture.Handler.Calls);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, RemoteFailureKind.Auth)]
    [InlineData(HttpStatusCode.Forbidden, RemoteFailureKind.AccessDenied)]
    [InlineData(HttpStatusCode.NotFound, RemoteFailureKind.NotFound)]
    public async Task AcquireAsync_GatewayError_IsMappedToTheFailureKind(HttpStatusCode status, RemoteFailureKind expected)
    {
        using var fixture = new CortexaFixture(_ => RemoteResponses.Status(status));

        var exception = await Assert.ThrowsAsync<RemoteSourceException>(
            () => fixture.Store.AcquireAsync(Request, TestSupport.Ct));

        Assert.Equal(expected, exception.Kind);
    }

    [Fact]
    public async Task ReleaseAsync_MatchingPrefix_DeletesTheArchive()
    {
        using var fixture = ServingZip();
        var path = await fixture.Store.AcquireAsync(Request, TestSupport.Ct);

        await fixture.Store.ReleaseAsync(CortexaArchiveRequest.KeyPrefix("github", "octo", "hello", "main"));

        Assert.False(File.Exists(path));
    }

    [Fact]
    public async Task ReleaseAsync_OtherBranchWithSharedNamePrefix_KeepsTheArchive()
    {
        using var fixture = ServingZip();
        var path = await fixture.Store.AcquireAsync(Request with { Branch = "main-2" }, TestSupport.Ct);

        await fixture.Store.ReleaseAsync(CortexaArchiveRequest.KeyPrefix("github", "octo", "hello", "main"));

        Assert.True(File.Exists(path));
    }

    [Fact]
    public async Task ReleaseAsync_ThenAcquireAgain_DownloadsAFreshArchive()
    {
        using var fixture = ServingZip();
        await fixture.Store.AcquireAsync(Request, TestSupport.Ct);
        await fixture.Store.ReleaseAsync(CortexaArchiveRequest.KeyPrefix("github", "octo", "hello", "main"));

        var path = await fixture.Store.AcquireAsync(Request, TestSupport.Ct);

        Assert.True(File.Exists(path));
        Assert.Equal(2, fixture.Handler.Calls.Count);
    }

    [Fact]
    public async Task ReleaseAsync_FailedDownload_DoesNotThrow()
    {
        using var fixture = new CortexaFixture(_ => RemoteResponses.Status(HttpStatusCode.InternalServerError));
        await Assert.ThrowsAsync<RemoteSourceException>(() => fixture.Store.AcquireAsync(Request, TestSupport.Ct));

        await fixture.Store.ReleaseAsync(CortexaArchiveRequest.KeyPrefix("github", "octo", "hello", "main"));

        Assert.Empty(fixture.ArchiveFiles);
    }

    [Fact]
    public async Task Dispose_WithLiveArchives_DeletesThem()
    {
        using var fixture = ServingZip();
        var first = await fixture.Store.AcquireAsync(Request, TestSupport.Ct);
        var second = await fixture.Store.AcquireAsync(Request with { Branch = "dev" }, TestSupport.Ct);

        fixture.Store.Dispose();

        Assert.False(File.Exists(first));
        Assert.False(File.Exists(second));
    }

    [Fact]
    public async Task AcquireAsync_FirstUse_SweepsLeftoverArchivesFromEarlierRuns()
    {
        using var fixture = ServingZip();
        Directory.CreateDirectory(fixture.ArchiveFolder);
        var leftover = Path.Combine(fixture.ArchiveFolder, "leftover.zip");
        await File.WriteAllBytesAsync(leftover, DefaultZip(), TestSupport.Ct);

        var path = await fixture.Store.AcquireAsync(Request, TestSupport.Ct);

        Assert.False(File.Exists(leftover));
        Assert.True(File.Exists(path));
    }

    [Fact]
    public void Key_SamePrefixDifferentVersion_StartsWithTheReleasePrefix()
    {
        var prefix = CortexaArchiveRequest.KeyPrefix("github", "octo", "hello", "main");

        Assert.StartsWith(prefix, Request.Key, StringComparison.Ordinal);
        Assert.StartsWith(prefix, (Request with { Version = "sha2" }).Key, StringComparison.Ordinal);
    }
}
