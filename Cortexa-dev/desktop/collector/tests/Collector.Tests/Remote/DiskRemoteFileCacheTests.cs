using System.Text;
using Collector.Application.Extraction;
using Collector.Domain.Enums;
using Collector.Infrastructure.Options;
using Collector.Infrastructure.Remote;
using Collector.Tests.Support;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace Collector.Tests.Remote;

public sealed class DiskRemoteFileCacheTests : IDisposable
{
    private const string RepoKey = "octo/hello";
    private const string Branch = "main";

    private readonly string _root = Path.Combine(Path.GetTempPath(), $"collector-remote-{Guid.NewGuid():N}");
    private readonly DiskRemoteFileCache _cache;

    public DiskRemoteFileCacheTests()
    {
        _cache = new DiskRemoteFileCache(MsOptions.Create(new RemoteSourceOptions { CacheRoot = _root }));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private string? Resolve(string path, string repoKey = RepoKey, string branch = Branch) =>
        _cache.ResolvePath(SourceType.Github, repoKey, branch, path);

    private string ResolveValid(string path) =>
        Resolve(path) ?? throw new InvalidOperationException($"Expected a valid path for '{path}'.");

    private string[] AllFiles() =>
        Directory.Exists(_root) ? Directory.GetFiles(_root, "*", SearchOption.AllDirectories) : [];

    private static MemoryStream Bytes(string text) => new(Encoding.UTF8.GetBytes(text));

    [Theory]
    [InlineData("README.md")]
    [InlineData("docs/guide.md")]
    [InlineData("docs\\guide.md")]
    [InlineData("src/deep/er/file.name.with.dots.cs")]
    [InlineData("contest.md")]
    [InlineData("console/readme.md")]
    public void ResolvePath_SafePath_StaysUnderRoot(string path)
    {
        var resolved = Resolve(path);

        Assert.NotNull(resolved);
        Assert.StartsWith(Path.GetFullPath(_root), resolved, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("../escape.md")]
    [InlineData("a/../../escape.md")]
    [InlineData("a/../b.md")]
    [InlineData("..")]
    [InlineData("a/./b.md")]
    [InlineData("/etc/passwd")]
    [InlineData("\\windows\\system32\\x.md")]
    [InlineData("\\\\server\\share\\x.md")]
    [InlineData("C:\\windows\\x.md")]
    [InlineData("C:x.md")]
    [InlineData("dir/file.md:stream")]
    [InlineData("file.md::$DATA")]
    [InlineData("a:b/c.md")]
    [InlineData("CON")]
    [InlineData("con.txt")]
    [InlineData("dir/NUL.md")]
    [InlineData("dir/aux.tar.gz")]
    [InlineData("COM1.md")]
    [InlineData("lpt9")]
    [InlineData("dir/")]
    [InlineData("a//b.md")]
    [InlineData("trailingdot.")]
    [InlineData("trailingspace.md ")]
    [InlineData("a/b?.md")]
    [InlineData("a/b*.md")]
    [InlineData("")]
    [InlineData("   ")]
    public void ResolvePath_UnsafePath_ReturnsNull(string path)
    {
        Assert.Null(Resolve(path));
    }

    [Fact]
    public void ResolvePath_RepoKeyDiffersOnlyByCase_ResolvesToSameLocation()
    {
        var lower = Resolve("a.md", "octo/hello");
        var mixed = Resolve("a.md", "Octo/Hello");

        Assert.Equal(lower, mixed);
    }

    [Fact]
    public void ResolvePath_DifferentBranches_ResolveToDifferentLocations()
    {
        Assert.NotEqual(Resolve("a.md", branch: "main"), Resolve("a.md", branch: "dev"));
    }

    [Fact]
    public void ResolvePath_DifferentRepositories_ResolveToDifferentLocations()
    {
        Assert.NotEqual(Resolve("a.md", "octo/one"), Resolve("a.md", "octo/two"));
    }

    [Fact]
    public async Task WriteAsync_ValidStream_WritesContentAndReportsSize()
    {
        const string Content = "# hello";
        var target = ResolveValid("docs/a.md");

        var result = await _cache.WriteAsync(target, Bytes(Content), TestSupport.Ct);

        Assert.False(result.ExceededLimit);
        Assert.Equal(Content.Length, result.SizeBytes);
        Assert.Equal(Content, await File.ReadAllTextAsync(target, TestSupport.Ct));
    }

    [Fact]
    public async Task WriteAsync_ValidStream_LeavesNoTemporaryFiles()
    {
        var target = ResolveValid("a.md");

        await _cache.WriteAsync(target, Bytes("x"), TestSupport.Ct);

        Assert.Equal([target], AllFiles());
    }

    [Fact]
    public async Task WriteAsync_ExistingFile_IsReplaced()
    {
        var target = ResolveValid("a.md");
        await _cache.WriteAsync(target, Bytes("old content"), TestSupport.Ct);

        await _cache.WriteAsync(target, Bytes("new"), TestSupport.Ct);

        Assert.Equal("new", await File.ReadAllTextAsync(target, TestSupport.Ct));
    }

    [Fact]
    public async Task WriteAsync_ExactlyMaxFileBytes_IsAccepted()
    {
        var target = ResolveValid("limit.md");
        using var content = new MemoryStream(new byte[FileContentGuard.MaxFileBytes]);

        var result = await _cache.WriteAsync(target, content, TestSupport.Ct);

        Assert.False(result.ExceededLimit);
        Assert.Equal(FileContentGuard.MaxFileBytes, new FileInfo(target).Length);
    }

    [Fact]
    public async Task WriteAsync_OneByteOverMaxFileBytes_ReportsExceededAndLeavesNoFiles()
    {
        var target = ResolveValid("over.md");
        using var content = new MemoryStream(new byte[FileContentGuard.MaxFileBytes + 1]);

        var result = await _cache.WriteAsync(target, content, TestSupport.Ct);

        Assert.True(result.ExceededLimit);
        Assert.False(File.Exists(target));
        Assert.Empty(AllFiles());
    }

    [Fact]
    public async Task WriteAsync_EndlessStream_StopsReadingAtLimitAndLeavesNoFiles()
    {
        var target = ResolveValid("endless.md");
        await using var content = new EndlessStream();

        var result = await _cache.WriteAsync(target, content, TestSupport.Ct);

        Assert.True(result.ExceededLimit);
        Assert.True(content.BytesServed <= FileContentGuard.MaxFileBytes + 1);
        Assert.Empty(AllFiles());
    }

    [Fact]
    public async Task WriteAsync_OverLimitReplacingExistingFile_KeepsPreviousContent()
    {
        var target = ResolveValid("keep.md");
        await _cache.WriteAsync(target, Bytes("previous"), TestSupport.Ct);
        using var content = new MemoryStream(new byte[FileContentGuard.MaxFileBytes + 1]);

        await _cache.WriteAsync(target, content, TestSupport.Ct);

        Assert.Equal("previous", await File.ReadAllTextAsync(target, TestSupport.Ct));
    }

    [Fact]
    public async Task WriteAsync_StreamFailsMidway_LeavesNoTemporaryFile()
    {
        var target = ResolveValid("fail.md");
        await using var content = new FailingStream();

        await Assert.ThrowsAsync<IOException>(() => _cache.WriteAsync(target, content, TestSupport.Ct));

        Assert.Empty(AllFiles());
    }

    [Fact]
    public async Task WriteAsync_PathOutsideRoot_Throws()
    {
        var outside = Path.Combine(Path.GetTempPath(), $"outside-{Guid.NewGuid():N}.md");

        await Assert.ThrowsAsync<InvalidOperationException>(() => _cache.WriteAsync(outside, Bytes("x"), TestSupport.Ct));

        Assert.False(File.Exists(outside));
    }

    [Fact]
    public async Task WriteAsync_TraversalOutOfRoot_Throws()
    {
        var traversal = Path.Combine(_root, "..", $"escape-{Guid.NewGuid():N}.md");

        await Assert.ThrowsAsync<InvalidOperationException>(() => _cache.WriteAsync(traversal, Bytes("x"), TestSupport.Ct));
    }

    [Fact]
    public async Task Exists_AfterWrite_ReturnsTrue()
    {
        var target = ResolveValid("a.md");
        await _cache.WriteAsync(target, Bytes("x"), TestSupport.Ct);

        Assert.True(_cache.Exists(target));
    }

    [Fact]
    public void Exists_FileNeverWritten_ReturnsFalse()
    {
        Assert.False(_cache.Exists(ResolveValid("missing.md")));
    }

    [Fact]
    public async Task Exists_FileOutsideRoot_ReturnsFalse()
    {
        var outside = Path.Combine(Path.GetTempPath(), $"outside-{Guid.NewGuid():N}.md");
        await File.WriteAllTextAsync(outside, "x", TestSupport.Ct);

        try
        {
            Assert.False(_cache.Exists(outside));
        }
        finally
        {
            File.Delete(outside);
        }
    }

    private sealed class EndlessStream : Stream
    {
        public long BytesServed { get; private set; }

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            BytesServed += buffer.Length;
            return ValueTask.FromResult(buffer.Length);
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private sealed class FailingStream : Stream
    {
        private bool _served;

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (_served)
            {
                throw new IOException("connection reset");
            }

            _served = true;
            buffer.Span[0] = 1;
            return ValueTask.FromResult(1);
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
