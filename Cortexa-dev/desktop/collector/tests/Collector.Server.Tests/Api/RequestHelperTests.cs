using System.Security.Claims;
using System.Text;
using Collector.Server.Api.Auth;
using Collector.Server.Api.Endpoints;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;

namespace Collector.Server.Tests.Api;

public class RequestHelperTests
{
    private const int MaxKeyLength = 8;
    private const long MaxBodyBytes = 16;

    [Theory]
    [InlineData("abc-123", true)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData("caf\u00E9", false)]
    [InlineData("tab\there", false)]
    [InlineData("123456789", false)]
    public void TryRead_SingleHeaderValue_AcceptsOnlyPrintableAsciiWithinLimit(string value, bool expected)
    {
        var request = RequestWithKey(value);

        var ok = IdempotencyKeys.TryRead(request, MaxKeyLength, out var key);

        Assert.Equal(expected, ok);
        Assert.Equal(expected ? value : string.Empty, key);
    }

    [Fact]
    public void TryRead_HeaderMissing_ReturnsFalse()
    {
        var request = new DefaultHttpContext().Request;

        var ok = IdempotencyKeys.TryRead(request, MaxKeyLength, out _);

        Assert.False(ok);
    }

    [Fact]
    public void TryRead_HeaderRepeated_ReturnsFalse()
    {
        var request = new DefaultHttpContext().Request;
        request.Headers[IdempotencyKeys.HeaderName] = new StringValues(["a", "b"]);

        var ok = IdempotencyKeys.TryRead(request, MaxKeyLength, out _);

        Assert.False(ok);
    }

    [Fact]
    public async Task ReadAsync_BodyWithinLimit_ReturnsBytes()
    {
        var request = RequestWithBody(new string('x', (int)MaxBodyBytes));

        var body = await RequestBodyReader.ReadAsync(request, MaxBodyBytes, TestContext.Current.CancellationToken);

        Assert.Equal(MaxBodyBytes, body!.Length);
    }

    [Fact]
    public async Task ReadAsync_DeclaredLengthOverLimit_ReturnsNull()
    {
        var request = RequestWithBody(new string('x', (int)MaxBodyBytes + 1));

        var body = await RequestBodyReader.ReadAsync(request, MaxBodyBytes, TestContext.Current.CancellationToken);

        Assert.Null(body);
    }

    [Fact]
    public async Task ReadAsync_ChunkedBodyOverLimit_ReturnsNull()
    {
        var request = RequestWithBody(new string('x', (int)MaxBodyBytes + 1));
        request.ContentLength = null;

        var body = await RequestBodyReader.ReadAsync(request, MaxBodyBytes, TestContext.Current.CancellationToken);

        Assert.Null(body);
    }

    [Theory]
    [InlineData("[\"jobs:submit\"]", true)]
    [InlineData("[\"other\",\"jobs:submit\"]", true)]
    [InlineData("jobs:submit", true)]
    [InlineData("[\"other\"]", false)]
    [InlineData("[not json", false)]
    [InlineData("jobs:submit-extra", false)]
    public void HasPermission_ClaimShapes_MatchesExactPermission(string claimValue, bool expected)
    {
        var principal = Principal(new Claim(CollectorClaims.Permissions, claimValue));

        var result = CollectorClaims.HasPermission(principal, CollectorClaims.JobsSubmit);

        Assert.Equal(expected, result);
    }

    [Fact]
    public void HasPermission_PermissionSplitAcrossClaims_FindsIt()
    {
        var principal = Principal(
            new Claim(CollectorClaims.Permissions, "other"),
            new Claim(CollectorClaims.Permissions, CollectorClaims.JobsSubmit));

        var result = CollectorClaims.HasPermission(principal, CollectorClaims.JobsSubmit);

        Assert.True(result);
    }

    [Theory]
    [InlineData(TestIdentity.UserId, "", false)]
    [InlineData("", TestIdentity.OrgId, false)]
    [InlineData(TestIdentity.UserId, TestIdentity.OrgId, true)]
    public void TryGetCaller_SubjectAndOrgClaims_RequiresBoth(string subject, string orgId, bool expected)
    {
        var principal = Principal(new Claim(CollectorClaims.Subject, subject), new Claim(CollectorClaims.OrgId, orgId));

        var ok = CollectorClaims.TryGetCaller(principal, out var caller);

        Assert.Equal(expected, ok);
        Assert.Equal(expected, caller is not null);
    }

    private static ClaimsPrincipal Principal(params Claim[] claims) => new(new ClaimsIdentity(claims, "test"));

    private static HttpRequest RequestWithKey(string value)
    {
        var request = new DefaultHttpContext().Request;
        request.Headers[IdempotencyKeys.HeaderName] = value;
        return request;
    }

    private static HttpRequest RequestWithBody(string content)
    {
        var bytes = Encoding.UTF8.GetBytes(content);
        var request = new DefaultHttpContext().Request;
        request.Body = new MemoryStream(bytes);
        request.ContentLength = bytes.Length;
        return request;
    }
}
