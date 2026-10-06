using Collector.Infrastructure.Auth;

namespace Collector.Tests.Auth;

public class RefreshCookieParserTests
{
    [Fact]
    public void Returns_value_up_to_first_semicolon()
    {
        var result = RefreshCookieParser.TryGetRefreshToken(["refresh_token=abc123; Path=/; HttpOnly; Secure"]);

        Assert.Equal("abc123", result);
    }

    [Fact]
    public void Picks_refresh_cookie_among_several_headers()
    {
        var result = RefreshCookieParser.TryGetRefreshToken(["other=1; Path=/", "refresh_token=xyz; Max-Age=600"]);

        Assert.Equal("xyz", result);
    }

    [Fact]
    public void Keeps_equals_signs_inside_value()
    {
        var result = RefreshCookieParser.TryGetRefreshToken(["refresh_token=ab==; Path=/"]);

        Assert.Equal("ab==", result);
    }

    [Theory]
    [InlineData("refresh_token=; Path=/")]
    [InlineData("refresh_token=abc; Max-Age=0")]
    [InlineData("refresh_token=abc; max-age = 0; Path=/")]
    [InlineData("other=abc")]
    [InlineData("refresh_token_x=abc")]
    [InlineData("garbage")]
    public void Treats_empty_expired_or_missing_cookie_as_absent(string header)
    {
        Assert.Null(RefreshCookieParser.TryGetRefreshToken([header]));
    }

    [Fact]
    public void Returns_null_when_no_headers()
    {
        Assert.Null(RefreshCookieParser.TryGetRefreshToken([]));
    }

    [Fact]
    public void Skips_expired_header_and_uses_later_valid_one()
    {
        var result = RefreshCookieParser.TryGetRefreshToken(["refresh_token=; Max-Age=0", "refresh_token=fresh"]);

        Assert.Equal("fresh", result);
    }
}
