using Collector.Application.Ports;
using Collector.Application.Upload;

namespace Collector.Tests.Upload;

public sealed class UploadErrorMapperTests
{
    public static TheoryData<int?, string?, UploadErrorKind, string?> Table() => new()
    {
        { null, null, UploadErrorKind.Network, null },
        { null, "some_code", UploadErrorKind.Network, null },
        { 401, null, UploadErrorKind.Session, null },
        { 401, "token_expired", UploadErrorKind.Session, null },
        { 400, "validation_failed", UploadErrorKind.Rejected, "validation_failed" },
        { 409, "duplicate", UploadErrorKind.Rejected, "duplicate" },
        { 409, "upload_in_progress", UploadErrorKind.InProgress, null },
        { 409, "idempotency_key_reused", UploadErrorKind.Rejected, "idempotency_key_reused" },
        { 502, null, UploadErrorKind.Unknown, null },
        { 499, "odd", UploadErrorKind.Rejected, "odd" },
        { 400, null, UploadErrorKind.Unknown, null },
        { 422, " ", UploadErrorKind.Unknown, null },
        { 500, "server_code", UploadErrorKind.Unknown, null },
        { 503, null, UploadErrorKind.Unknown, null },
        { 399, "code", UploadErrorKind.Unknown, null },
    };

    [Theory]
    [MemberData(nameof(Table))]
    public void Map_StatusAndCode_ReturnsExpectedError(int? status, string? code, UploadErrorKind kind, string? rejectedCode)
    {
        var exception = new KnowledgeUploadException(status, code, "message");

        var error = UploadErrorMapper.Map(exception);

        Assert.Equal(new UploadError(kind, rejectedCode), error);
    }

    [Theory]
    [InlineData(UploadErrorKind.Session, null, "session")]
    [InlineData(UploadErrorKind.Network, null, "network")]
    [InlineData(UploadErrorKind.Unknown, null, "unknown")]
    [InlineData(UploadErrorKind.Rejected, "validation_failed", "rejected:validation_failed")]
    public void Describe_Error_ReturnsStableText(UploadErrorKind kind, string? code, string expected)
    {
        var text = UploadErrorMapper.Describe(new UploadError(kind, code));

        Assert.Equal(expected, text);
    }

    [Theory]
    [InlineData(UploadErrorKind.Network, true)]
    [InlineData(UploadErrorKind.Unknown, true)]
    [InlineData(UploadErrorKind.InProgress, true)]
    [InlineData(UploadErrorKind.Session, true)]
    [InlineData(UploadErrorKind.Rejected, false)]
    public void CanRetry_Kind_MatchesRetryability(UploadErrorKind kind, bool expected)
    {
        Assert.Equal(expected, new UploadError(kind, null).CanRetry);
    }

    [Theory]
    [InlineData(502)]
    [InlineData(503)]
    public void Map_GatewayErrors_AreRetryable(int status)
    {
        var error = UploadErrorMapper.Map(new KnowledgeUploadException(status, null, "message"));

        Assert.True(error.CanRetry);
    }

    [Fact]
    public void Map_IdempotencyKeyReused_IsTerminal()
    {
        var error = UploadErrorMapper.Map(new KnowledgeUploadException(409, "idempotency_key_reused", "message"));

        Assert.False(error.CanRetry);
    }

    [Theory]
    [InlineData(UploadErrorKind.Session, null)]
    [InlineData(UploadErrorKind.Network, null)]
    [InlineData(UploadErrorKind.Unknown, null)]
    [InlineData(UploadErrorKind.InProgress, null)]
    [InlineData(UploadErrorKind.Rejected, "validation_failed")]
    [InlineData(UploadErrorKind.Rejected, "idempotency_key_reused")]
    public void Parse_DescribedError_RoundTrips(UploadErrorKind kind, string? code)
    {
        var original = new UploadError(kind, code);

        var parsed = UploadErrorMapper.Parse(UploadErrorMapper.Describe(original));

        Assert.Equal(original, parsed);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("garbage")]
    [InlineData("rejected:")]
    public void Parse_UnrecognizedText_ReturnsUnknown(string? text)
    {
        Assert.Equal(new UploadError(UploadErrorKind.Unknown, null), UploadErrorMapper.Parse(text));
    }
}
