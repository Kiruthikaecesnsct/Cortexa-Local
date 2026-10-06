using System.Net;
using System.Net.Http.Headers;
using Collector.Application.Auth;
using Collector.Infrastructure.Auth;

namespace Collector.Tests.Auth;

public class AuthResponseMapperTests
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(30);

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, "INVALID_CREDENTIALS", AuthFailureKind.InvalidCredentials)]
    [InlineData(HttpStatusCode.Unauthorized, "INVALID_REFRESH_TOKEN", AuthFailureKind.InvalidRefreshToken)]
    [InlineData(HttpStatusCode.Unauthorized, null, AuthFailureKind.UnexpectedResponse)]
    [InlineData(HttpStatusCode.Forbidden, null, AuthFailureKind.Forbidden)]
    [InlineData(HttpStatusCode.TooManyRequests, "ACCOUNT_LOCKED", AuthFailureKind.AccountLocked)]
    [InlineData(HttpStatusCode.TooManyRequests, null, AuthFailureKind.RateLimited)]
    [InlineData(HttpStatusCode.BadRequest, "VALIDATION_ERROR", AuthFailureKind.InvalidInput)]
    [InlineData(HttpStatusCode.InternalServerError, null, AuthFailureKind.UnexpectedResponse)]
    public void Maps_status_and_error_code(HttpStatusCode status, string? code, AuthFailureKind expected)
    {
        var failure = AuthResponseMapper.Map(status, code, null);

        Assert.Equal(expected, failure.Kind);
    }

    [Fact]
    public void Carries_retry_after_for_locked_and_rate_limited()
    {
        var locked = AuthResponseMapper.Map(HttpStatusCode.TooManyRequests, "ACCOUNT_LOCKED", Wait);
        var limited = AuthResponseMapper.Map(HttpStatusCode.TooManyRequests, null, Wait);

        Assert.Equal(Wait, locked.RetryAfter);
        Assert.Equal(Wait, limited.RetryAfter);
    }

    [Fact]
    public void Parses_retry_after_seconds()
    {
        var header = new RetryConditionHeaderValue(Wait);

        Assert.Equal(Wait, RetryAfterParser.Parse(header, DateTimeOffset.UnixEpoch));
    }

    [Fact]
    public void Parses_retry_after_http_date()
    {
        var now = new DateTimeOffset(2026, 10, 6, 12, 0, 0, TimeSpan.Zero);
        var header = new RetryConditionHeaderValue(now + Wait);

        Assert.Equal(Wait, RetryAfterParser.Parse(header, now));
    }

    [Fact]
    public void Clamps_past_http_date_to_zero()
    {
        var now = new DateTimeOffset(2026, 10, 6, 12, 0, 0, TimeSpan.Zero);
        var header = new RetryConditionHeaderValue(now - Wait);

        Assert.Equal(TimeSpan.Zero, RetryAfterParser.Parse(header, now));
    }

    [Fact]
    public void Returns_null_without_header()
    {
        Assert.Null(RetryAfterParser.Parse(null, DateTimeOffset.UnixEpoch));
    }
}
