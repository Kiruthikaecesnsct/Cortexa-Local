using Collector.Infrastructure.Remote.Cortexa;

namespace Collector.Tests.Remote.Cortexa;

public class CortexaBlobKeyCodecTests
{
    [Theory]
    [InlineData("main", "3f2a9c1e55", "README.md")]
    [InlineData("feature/ünï-branch", "", "docs/design notes/überblick.md")]
    [InlineData("release|1.0", "2026-10-02T00:00:00Z", "a/b/c/日本語 ファイル.txt")]
    public void Encode_ThenParse_RoundTripsEveryField(string branch, string version, string path)
    {
        var key = CortexaBlobKeyCodec.Parse(CortexaBlobKeyCodec.Encode(branch, version, path));

        Assert.Equal(new CortexaBlobKey(branch, version, path), key);
    }

    [Fact]
    public void Encode_NewVersionOfSameFile_ProducesDifferentKey()
    {
        var older = CortexaBlobKeyCodec.Encode("main", "sha-1", "a.md");
        var newer = CortexaBlobKeyCodec.Encode("main", "sha-2", "a.md");

        Assert.NotEqual(older, newer);
    }

    [Theory]
    [InlineData("bad\u0001name", "v", "a.md")]
    [InlineData("bad\u0002name", "v", "a.md")]
    [InlineData("main", "bad\u0001v", "a.md")]
    [InlineData("main", "bad\u0002v", "a.md")]
    [InlineData("main", "v", "bad\u0001.md")]
    [InlineData("main", "v", "bad\u0002.md")]
    public void Encode_FieldWithReservedCharacter_Throws(string branch, string version, string path)
    {
        Assert.Throws<ArgumentException>(() => CortexaBlobKeyCodec.Encode(branch, version, path));
    }

    [Theory]
    [InlineData("a/b.md", true)]
    [InlineData("", true)]
    [InlineData("a\u0001b", false)]
    [InlineData("a\u0002b", false)]
    public void CanEncode_Value_ReflectsReservedCharacters(string value, bool expected)
    {
        Assert.Equal(expected, CortexaBlobKeyCodec.CanEncode(value));
    }

    [Theory]
    [InlineData("no-delimiters")]
    [InlineData("main\u0001missing-pair")]
    [InlineData("main\u0002version-before-branch")]
    public void Parse_MissingDelimiter_ThrowsFormatException(string blobKey)
    {
        Assert.Throws<FormatException>(() => CortexaBlobKeyCodec.Parse(blobKey));
    }

    [Fact]
    public void Parse_PathIsLast_KeepsPathWithSecondDelimiterIntact()
    {
        var key = CortexaBlobKeyCodec.Parse("main\u0001v1\u0002dir/file\u0002name.md");

        Assert.Equal(new CortexaBlobKey("main", "v1", "dir/file\u0002name.md"), key);
    }
}
