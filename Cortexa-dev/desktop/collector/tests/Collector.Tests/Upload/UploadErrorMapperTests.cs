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
}
