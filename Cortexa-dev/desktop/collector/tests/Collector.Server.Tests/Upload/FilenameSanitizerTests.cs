using Collector.Server.Application.Upload;

namespace Collector.Server.Tests.Upload;

public class FilenameSanitizerTests
{
    [Theory]
    [InlineData("pa\u0000per\u0007.pdf")]
    [InlineData("pa\tper\r\n.pdf")]
    [InlineData("pa\u202Eper\u202D.pdf")]
    [InlineData("pa\u2066per\u2069.pdf")]
    [InlineData("pa\u200Eper\u200F.pdf")]
    [InlineData("pa\u200Bper\u200C\u200D.pdf")]
    [InlineData("\uFEFFpaper.pdf")]
    [InlineData("pa\"per'`.pdf")]
    [InlineData("pa\u2018per\u2019\u201C\u201D.pdf")]
    [InlineData("pa\u2028per\u2029.pdf")]
    public void Clean_UnsafeCharacters_AreStripped(string filename)
    {
        var cleaned = FilenameSanitizer.Clean(filename);

        Assert.Equal("paper.pdf", cleaned);
    }

    [Fact]
    public void Clean_OrdinaryUnicodeName_IsPreserved()
    {
        const string Filename = "r\u00E9sum\u00E9 \u8AD6\u6587.pdf";

        var cleaned = FilenameSanitizer.Clean(Filename);

        Assert.Equal(Filename, cleaned);
    }

    [Fact]
    public void Clean_SurroundingWhitespace_IsTrimmed()
    {
        var cleaned = FilenameSanitizer.Clean("  paper.pdf \t");

        Assert.Equal("paper.pdf", cleaned);
    }

    [Fact]
    public void Clean_NameLongerThanCap_IsTruncatedToCap()
    {
        var cleaned = FilenameSanitizer.Clean(new string('a', UploadLimits.MaxFilenameLength + 44));

        Assert.Equal(UploadLimits.MaxFilenameLength, cleaned.Length);
    }

    [Fact]
    public void Clean_SurrogatePairStraddlingCap_IsNotSplit()
    {
        var filename = new string('a', UploadLimits.MaxFilenameLength - 1) + "\U0001F600";

        var cleaned = FilenameSanitizer.Clean(filename);

        Assert.Equal(new string('a', UploadLimits.MaxFilenameLength - 1), cleaned);
    }

    [Fact]
    public void Clean_StrippedCharactersDoNotCountAgainstCap()
    {
        var filename = new string('\u200B', UploadLimits.MaxFilenameLength) + "paper.pdf";

        var cleaned = FilenameSanitizer.Clean(filename);

        Assert.Equal("paper.pdf", cleaned);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\u200B\u202E\"'")]
    public void TryClean_NothingLeftAfterStripping_ReturnsFalse(string? filename)
    {
        var ok = FilenameSanitizer.TryClean(filename, out var cleaned);

        Assert.False(ok);
        Assert.Empty(cleaned);
    }

    [Fact]
    public void TryClean_ValidName_ReturnsTrueWithCleanedName()
    {
        var ok = FilenameSanitizer.TryClean("a\u200B.py", out var cleaned);

        Assert.True(ok);
        Assert.Equal("a.py", cleaned);
    }
}
