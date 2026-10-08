using Collector.Application.Extraction;
using Collector.Application.Remote;
using Collector.Tests.Support;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace Collector.Tests.Remote;

public class RemoteFileFilterTests
{
    private readonly RemoteFileFilter _filter = new(MsOptions.Create(new RemoteFetchOptions()));

    [Theory]
    [InlineData("README.md")]
    [InlineData("docs/guide.TXT")]
    [InlineData("notes/spec.rst")]
    [InlineData("paper.pdf")]
    [InlineData("thesis.docx")]
    [InlineData("src/app.py")]
    [InlineData("src/Program.cs")]
    public void Keep_SupportedFile_ReturnsTrue(string path)
    {
        Assert.True(_filter.Keep(RemoteData.Entry(path, "sha")));
    }

    [Theory]
    [InlineData("logo.png")]
    [InlineData("data.csv")]
    [InlineData("LICENSE")]
    [InlineData("archive.zip")]
    public void Keep_UnsupportedTextLikeFile_ReturnsFalse(string path)
    {
        Assert.False(_filter.Keep(RemoteData.Entry(path, "sha")));
    }

    [Theory]
    [InlineData("node_modules/pkg/readme.md")]
    [InlineData("src/.git/config.md")]
    [InlineData("a/bin/notes.md")]
    [InlineData("a\\obj\\notes.md")]
    [InlineData("VENDOR/lib/readme.md")]
    [InlineData(".venv/lib/site.py")]
    public void Keep_FileInsideExcludedDirectory_ReturnsFalse(string path)
    {
        Assert.False(_filter.Keep(RemoteData.Entry(path, "sha")));
    }

    [Fact]
    public void Keep_FileOverSizeLimit_ReturnsFalse()
    {
        var entry = RemoteData.Entry("big.md", "sha", FileContentGuard.MaxFileBytes + 1);

        Assert.False(_filter.Keep(entry));
    }

    [Fact]
    public void Keep_FileAtSizeLimit_ReturnsTrue()
    {
        var entry = RemoteData.Entry("limit.md", "sha", FileContentGuard.MaxFileBytes);

        Assert.True(_filter.Keep(entry));
    }

    [Fact]
    public void Keep_UnknownSize_ReturnsTrue()
    {
        Assert.True(_filter.Keep(RemoteData.Entry("unknown.md", "sha", null)));
    }

    [Fact]
    public void Keep_CustomOptions_UsesConfiguredExtensionsAndDirectories()
    {
        var options = new RemoteFetchOptions { TextExtensions = [".log"], ExcludedDirectories = ["secret"] };
        var filter = new RemoteFileFilter(MsOptions.Create(options));

        Assert.True(filter.Keep(RemoteData.Entry("a/run.log", "sha")));
        Assert.False(filter.Keep(RemoteData.Entry("a/readme.md", "sha")));
        Assert.False(filter.Keep(RemoteData.Entry("secret/run.log", "sha")));
    }
}
