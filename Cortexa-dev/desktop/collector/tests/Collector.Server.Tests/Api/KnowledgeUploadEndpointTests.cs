using System.Net;
using System.Text.Json;
using Collector.Domain.Enums;
using Collector.Domain.Serialization;
using Collector.Domain.Upload;
using Collector.Server.Application.Ports;
using Collector.Server.Application.Upload;
using Collector.Server.Tests.Upload;
using Microsoft.IdentityModel.Tokens;

namespace Collector.Server.Tests.Api;

public class KnowledgeUploadEndpointTests
{
    private const string OtherIssuer = "someone-else";
    private const string OtherSigningKey = "another-signing-key-that-is-long-enough-for-hs256-use";
    private const string LocationPrefix = "/collector/batches/";
    private const string SecretExcerpt = "EXCERPT-MUST-NOT-BE-LOGGED";
    private const string SecretKey = "IDEMPOTENCY-KEY-MUST-NOT-BE-LOGGED";
    private const int OverLongKeyLength = 129;
    private const long TinyBodyLimit = 256;

    [Fact]
    public async Task Post_ValidUpload_Returns201WithBatchDocumentIdsAndLocation()
    {
        await using var factory = new CollectorServerFactory();
        var call = new UploadCall().WithRequest(UploadRequests.Valid(UploadRequests.PaperDocument(), UploadRequests.CodeDocument()));
        var batchId = DeterministicIds.BatchId(TestIdentity.UserId, TestIdentity.IdempotencyKey);

        using var response = await factory.PostAsync(call);

        var result = await ReadResultAsync(response);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(batchId, result.BatchId);
        Assert.Equal(2, result.DocumentIds.Count);
        Assert.Equal(LocationPrefix + batchId, response.Headers.Location!.OriginalString);
    }

    [Fact]
    public async Task Post_ValidUpload_WritesRowsAndPublishesForTheCaller()
    {
        await using var factory = new CollectorServerFactory();

        using var response = await factory.PostAsync(new UploadCall());

        var saga = Assert.Single(factory.Store.Sagas.Values);
        Assert.Equal(TestIdentity.OrgId, saga.OrgId);
        Assert.Equal(TestIdentity.UserId, saga.OwnerUserId);
        Assert.Single(factory.Store.Documents);
        Assert.Single(factory.Publisher.Published);
    }

    [Fact]
    public async Task Post_SameKeyAndBodyTwice_SecondReturns200WithSameBodyAndNoNewWrites()
    {
        await using var factory = new CollectorServerFactory();
        var call = new UploadCall();
        using var first = await factory.PostAsync(call);
        var firstBody = await ReadTextAsync(first);
        var writesAfterFirst = factory.Store.WriteCalls;

        using var second = await factory.PostAsync(call);

        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Equal(firstBody, await ReadTextAsync(second));
        Assert.Equal(writesAfterFirst, factory.Store.WriteCalls);
        Assert.Single(factory.Publisher.Published);
    }

    [Fact]
    public async Task Post_NoToken_Returns401AndTouchesNothing()
    {
        await using var factory = new CollectorServerFactory();

        using var response = await factory.PostAsync(new UploadCall { Token = null });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(0, factory.Users.CallCount);
        Assert.Equal(0, factory.Store.WriteCalls);
    }

    [Theory]
    [MemberData(nameof(InvalidTokens))]
    public async Task Post_InvalidToken_Returns401(string scenario, string token)
    {
        await using var factory = new CollectorServerFactory();

        using var response = await factory.PostAsync(new UploadCall { Token = token });

        Assert.True(response.StatusCode == HttpStatusCode.Unauthorized, scenario);
        Assert.Equal(0, factory.Store.WriteCalls);
    }

    [Theory]
    [MemberData(nameof(ForbiddenTokens))]
    public async Task Post_ValidTokenWithoutRequiredClaims_Returns403(string scenario, TokenSpec spec)
    {
        await using var factory = new CollectorServerFactory();

        using var response = await factory.PostAsync(new UploadCall { Token = TestJwtFactory.Create(spec) });

        Assert.True(response.StatusCode == HttpStatusCode.Forbidden, scenario);
        Assert.Equal(0, factory.Store.WriteCalls);
    }

    [Fact]
    public async Task Post_StampMismatch_Returns403AccountInvalid()
    {
        await using var factory = new CollectorServerFactory();
        factory.Users.Status = new UserStatus(true, Guid.NewGuid());

        using var response = await factory.PostAsync(new UploadCall());

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("account_invalid", await ReadErrorCodeAsync(response));
        Assert.Equal(0, factory.Store.WriteCalls);
    }

    [Fact]
    public async Task Post_DisabledUser_Returns403AccountInvalid()
    {
        await using var factory = new CollectorServerFactory();
        factory.Users.Status = new UserStatus(false, TestIdentity.Stamp);

        using var response = await factory.PostAsync(new UploadCall());

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("account_invalid", await ReadErrorCodeAsync(response));
    }

    [Fact]
    public async Task Post_IdentityLookupFailsWithNoCache_Returns503()
    {
        await using var factory = new CollectorServerFactory();
        factory.Users.Status = null;

        using var response = await factory.PostAsync(new UploadCall());

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("account_status_unavailable", await ReadErrorCodeAsync(response));
        Assert.Equal(0, factory.Store.WriteCalls);
    }

    [Fact]
    public async Task Post_FailedIdentityLookup_IsNotCached()
    {
        await using var factory = new CollectorServerFactory();
        factory.Users.Status = null;
        using var failed = await factory.PostAsync(new UploadCall());
        factory.Users.Status = new UserStatus(true, TestIdentity.Stamp);

        using var recovered = await factory.PostAsync(new UploadCall());

        Assert.Equal(HttpStatusCode.Created, recovered.StatusCode);
    }

    [Fact]
    public async Task Post_TwoRequestsWithinCacheWindow_LooksUpUserOnce()
    {
        await using var factory = new CollectorServerFactory();

        using var first = await factory.PostAsync(new UploadCall { IdempotencyKey = "key-a" });
        using var second = await factory.PostAsync(new UploadCall { IdempotencyKey = "key-b" });

        Assert.Equal(HttpStatusCode.Created, second.StatusCode);
        Assert.Equal(1, factory.Users.CallCount);
    }

    [Fact]
    public async Task Post_CacheDisabled_LooksUpUserOnEveryRequest()
    {
        var settings = new Dictionary<string, string?> { ["Identity:StatusCacheSeconds"] = "0" };
        await using var factory = new CollectorServerFactory(settings);

        using var first = await factory.PostAsync(new UploadCall { IdempotencyKey = "key-a" });
        using var second = await factory.PostAsync(new UploadCall { IdempotencyKey = "key-b" });

        Assert.Equal(2, factory.Users.CallCount);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Post_MissingOrBlankIdempotencyKey_Returns400(string? key)
    {
        await using var factory = new CollectorServerFactory();

        using var response = await factory.PostAsync(new UploadCall { IdempotencyKey = key });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("invalid_idempotency_key", await ReadErrorCodeAsync(response));
    }

    [Fact]
    public async Task Post_OverLongIdempotencyKey_Returns400()
    {
        await using var factory = new CollectorServerFactory();

        using var response = await factory.PostAsync(new UploadCall { IdempotencyKey = new string('k', OverLongKeyLength) });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, factory.Store.WriteCalls);
    }

    [Theory]
    [InlineData("{ not json")]
    [InlineData("null")]
    [InlineData("{}")]
    public async Task Post_MalformedOrIncompleteJson_Returns400InvalidBody(string body)
    {
        await using var factory = new CollectorServerFactory();

        using var response = await factory.PostAsync(new UploadCall { Body = body });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("invalid_body", await ReadErrorCodeAsync(response));
    }

    [Fact]
    public async Task Post_UnknownEnumValue_Returns400InvalidBody()
    {
        await using var factory = new CollectorServerFactory();
        var body = UploadCall.Serialize(UploadRequests.Valid()).Replace("\"claude\"", "\"banana\"", StringComparison.Ordinal);

        using var response = await factory.PostAsync(new UploadCall { Body = body });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("invalid_body", await ReadErrorCodeAsync(response));
    }

    [Fact]
    public async Task Post_BodyOverConfiguredLimit_Returns413()
    {
        var settings = new Dictionary<string, string?> { ["Upload:MaxRequestBytes"] = TinyBodyLimit.ToString() };
        await using var factory = new CollectorServerFactory(settings);

        using var response = await factory.PostAsync(new UploadCall());

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        Assert.Equal("payload_too_large", await ReadErrorCodeAsync(response));
    }

    [Fact]
    public async Task Post_RuleViolation_Returns422WithFieldPathsAndWritesNothing()
    {
        await using var factory = new CollectorServerFactory();
        var item = UploadRequests.Item(KnowledgeKind.Layer, UnitKind.Section);
        var call = new UploadCall().WithRequest(UploadRequests.WithItem(SourceKind.Paper, item));

        using var response = await factory.PostAsync(call);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Contains("documents[0].knowledge_items[0].kind", await ReadErrorFieldsAsync(response));
        Assert.Equal(0, factory.Store.WriteCalls);
    }

    [Fact]
    public async Task Post_RuleViolationOnLongValue_ResponseDoesNotEchoTheValue()
    {
        await using var factory = new CollectorServerFactory();
        var item = UploadRequests.PaperSection() with { Excerpt = SecretExcerpt + new string('x', UploadLimits.MaxExcerptLength) };
        var call = new UploadCall().WithRequest(UploadRequests.WithItem(SourceKind.Paper, item));

        using var response = await factory.PostAsync(call);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.DoesNotContain(SecretExcerpt, await ReadTextAsync(response), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("/health/live")]
    [InlineData("/health/ready")]
    public async Task Get_HealthEndpoints_StayAnonymous(string path)
    {
        await using var factory = new CollectorServerFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(path, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Post_AcrossSuccessAndFailurePaths_LogsNeverContainExcerptTokenOrIdempotencyKey()
    {
        var settings = new Dictionary<string, string?>
        {
            ["Logging:LogLevel:Default"] = "Trace",
            ["Logging:LogLevel:Microsoft"] = "Trace",
            ["Identity:StatusCacheSeconds"] = "0"
        };
        await using var factory = new CollectorServerFactory(settings);
        var token = TestJwtFactory.Create();
        var item = UploadRequests.PaperSection() with { Excerpt = SecretExcerpt };
        var request = UploadRequests.WithItem(SourceKind.Paper, item);
        var call = new UploadCall { Token = token, IdempotencyKey = SecretKey }.WithRequest(request);

        using var created = await factory.PostAsync(call);
        using var replayed = await factory.PostAsync(call);
        using var rejected = await factory.PostAsync(call with { Token = token + "x" });
        factory.Users.Status = new UserStatus(false, TestIdentity.Stamp);
        using var disabled = await factory.PostAsync(call);
        using var invalid = await factory.PostAsync(call.WithRequest(InvalidRequestCarrying(SecretExcerpt + SecretKey)));

        Assert.NotEmpty(factory.Logs.Messages);
        Assert.DoesNotContain(factory.Logs.Messages, message => message.Contains(SecretExcerpt, StringComparison.Ordinal));
        Assert.DoesNotContain(factory.Logs.Messages, message => message.Contains(token, StringComparison.Ordinal));
        Assert.DoesNotContain(factory.Logs.Messages, message => message.Contains(SecretKey, StringComparison.Ordinal));
    }

    private static KnowledgeUploadRequest InvalidRequestCarrying(string title) =>
        UploadRequests.WithItem(
            SourceKind.Paper,
            UploadRequests.Item(KnowledgeKind.Layer, UnitKind.Section) with { Title = title });

    public static TheoryData<string, string> InvalidTokens() => new()
    {
        { "expired", TestJwtFactory.Create(new TokenSpec { ExpiresIn = TimeSpan.FromHours(-1) }) },
        { "bad signature", TestJwtFactory.Create(new TokenSpec { SigningKey = OtherSigningKey }) },
        { "alg none", TestJwtFactory.CreateUnsigned() },
        { "wrong issuer", TestJwtFactory.Create(new TokenSpec { Issuer = OtherIssuer }) },
        { "wrong audience", TestJwtFactory.Create(new TokenSpec { Audience = OtherIssuer }) },
        { "hs512 instead of hs256", TestJwtFactory.Create(new TokenSpec { Algorithm = SecurityAlgorithms.HmacSha512 }) },
        { "garbage", "not.a.jwt" }
    };

    public static TheoryData<string, TokenSpec> ForbiddenTokens() => new()
    {
        { "no jobs:submit permission", new TokenSpec { Permissions = ["jobs:read"] } },
        { "no permissions claim", new TokenSpec { Permissions = null } },
        { "no org_id", new TokenSpec { OrgId = null } },
        { "no stamp", new TokenSpec { Stamp = null } },
        { "subject not a guid", new TokenSpec { Subject = "not-a-guid" } }
    };

    private static async Task<string> ReadTextAsync(HttpResponseMessage response) =>
        await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

    private static async Task<KnowledgeUploadResult> ReadResultAsync(HttpResponseMessage response) =>
        JsonSerializer.Deserialize<KnowledgeUploadResult>(await ReadTextAsync(response), CollectorJson.Options)!;

    private static async Task<string?> ReadErrorCodeAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await ReadTextAsync(response));
        return document.RootElement.GetProperty("error").GetString();
    }

    private static async Task<IReadOnlyList<string?>> ReadErrorFieldsAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await ReadTextAsync(response));
        return [.. document.RootElement.GetProperty("errors").EnumerateArray().Select(e => e.GetProperty("field").GetString())];
    }
}
