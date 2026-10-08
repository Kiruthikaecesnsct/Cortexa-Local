using Collector.Infrastructure.Remote;

namespace Collector.Tests.Remote;

public class LinkHeaderParserTests
{
    private static readonly Uri RequestUri = new("https://api.github.com/user/repos?per_page=100");

    [Fact]
    public void ParseNext_NextAndLast_ReturnsNext()
    {
        string[] headers =
        [
            "<https://api.github.com/user/repos?page=2>; rel=\"next\", <https://api.github.com/user/repos?page=5>; rel=\"last\"",
        ];

        var next = LinkHeaderParser.ParseNext(headers, RequestUri);

        Assert.Equal(new Uri("https://api.github.com/user/repos?page=2"), next);
    }

    [Fact]
    public void ParseNext_NextListedAfterPrev_ReturnsNext()
    {
        string[] headers =
        [
            "<https://api.github.com/user/repos?page=1>; rel=\"prev\", <https://api.github.com/user/repos?page=3>; rel=\"next\"",
        ];

        var next = LinkHeaderParser.ParseNext(headers, RequestUri);

        Assert.Equal(new Uri("https://api.github.com/user/repos?page=3"), next);
    }

    [Fact]
    public void ParseNext_NoNextRelation_ReturnsNull()
    {
        string[] headers = ["<https://api.github.com/user/repos?page=1>; rel=\"prev\""];

        Assert.Null(LinkHeaderParser.ParseNext(headers, RequestUri));
    }

    [Fact]
    public void ParseNext_NoHeaders_ReturnsNull()
    {
        Assert.Null(LinkHeaderParser.ParseNext([], RequestUri));
    }

    [Fact]
    public void ParseNext_UnquotedRel_ReturnsNext()
    {
        string[] headers = ["<https://api.github.com/user/repos?page=2>; rel=next"];

        Assert.Equal(new Uri("https://api.github.com/user/repos?page=2"), LinkHeaderParser.ParseNext(headers, RequestUri));
    }

    [Fact]
    public void ParseNext_RelNextish_ReturnsNull()
    {
        string[] headers = ["<https://api.github.com/user/repos?page=2>; rel=\"nextish\""];

        Assert.Null(LinkHeaderParser.ParseNext(headers, RequestUri));
    }

    [Fact]
    public void ParseNext_RelativeTarget_ResolvesAgainstRequest()
    {
        string[] headers = ["</user/repos?page=2>; rel=\"next\""];

        var next = LinkHeaderParser.ParseNext(headers, RequestUri);

        Assert.Equal(new Uri("https://api.github.com/user/repos?page=2"), next);
    }

    [Theory]
    [InlineData("<https://evil.example/user/repos?page=2>; rel=\"next\"")]
    [InlineData("<http://api.github.com/user/repos?page=2>; rel=\"next\"")]
    [InlineData("<https://api.github.com:8443/user/repos?page=2>; rel=\"next\"")]
    public void ParseNext_DifferentOrigin_ReturnsNull(string header)
    {
        Assert.Null(LinkHeaderParser.ParseNext([header], RequestUri));
    }

    [Fact]
    public void ParseNext_NextSplitAcrossHeaderValues_FindsItInLaterValue()
    {
        string[] headers =
        [
            "<https://api.github.com/user/repos?page=1>; rel=\"prev\"",
            "<https://api.github.com/user/repos?page=3>; rel=\"next\"",
        ];

        var next = LinkHeaderParser.ParseNext(headers, RequestUri);

        Assert.Equal(new Uri("https://api.github.com/user/repos?page=3"), next);
    }

    [Fact]
    public void ParseNext_HostCaseDiffers_ReturnsNext()
    {
        string[] headers = ["<https://API.GITHUB.com/user/repos?page=2>; rel=\"next\""];

        Assert.NotNull(LinkHeaderParser.ParseNext(headers, RequestUri));
    }
}
