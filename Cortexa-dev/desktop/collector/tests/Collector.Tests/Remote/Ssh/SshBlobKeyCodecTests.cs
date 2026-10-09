using Collector.Infrastructure.Remote.Ssh;

namespace Collector.Tests.Remote.Ssh;

public class SshBlobKeyCodecTests
{
    [Theory]
    [InlineData("README.md", 0L, 0L)]
    [InlineData("docs/design notes/überblick.md", 638_000_000_000_000_000L, 4096L)]
    [InlineData("a/b/c/日本語 ファイル.txt", long.MaxValue, long.MaxValue)]
    public void Encode_ThenParse_RoundTripsEveryField(string path, long mtimeTicks, long sizeBytes)
    {
        var key = SshBlobKeyCodec.Parse(SshBlobKeyCodec.Encode(path, mtimeTicks, sizeBytes));

        Assert.Equal(new SshBlobKey(path, mtimeTicks, sizeBytes), key);
    }

    [Fact]
    public void Encode_SameFileWithNewerMtime_ProducesDifferentKey()
    {
        var older = SshBlobKeyCodec.Encode("src/app.cs", 100, 10);
        var newer = SshBlobKeyCodec.Encode("src/app.cs", 200, 10);

        Assert.NotEqual(older, newer);
    }

    [Theory]
    [InlineData("bad\u0001name")]
    [InlineData("bad\u0002name")]
    public void Encode_PathWithReservedCharacter_Throws(string path)
    {
        Assert.False(SshBlobKeyCodec.CanEncode(path));
        Assert.Throws<ArgumentException>(() => SshBlobKeyCodec.Encode(path, 1, 1));
    }

    [Theory]
    [InlineData("no-delimiters")]
    [InlineData("path\u0001missing-pair")]
    public void Parse_MissingDelimiter_ThrowsFormatException(string blobKey)
    {
        Assert.Throws<FormatException>(() => SshBlobKeyCodec.Parse(blobKey));
    }

    [Theory]
    [InlineData("path\u0001abc\u000210")]
    [InlineData("path\u000110\u0002xyz")]
    public void Parse_NonNumericField_ThrowsFormatException(string blobKey)
    {
        Assert.Throws<FormatException>(() => SshBlobKeyCodec.Parse(blobKey));
    }
}
