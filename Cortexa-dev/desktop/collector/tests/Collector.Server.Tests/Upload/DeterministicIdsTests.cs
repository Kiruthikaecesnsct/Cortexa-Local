using Collector.Server.Application.Upload;

namespace Collector.Server.Tests.Upload;

public class DeterministicIdsTests
{
    private const string UserA = "user-a";
    private const string UserB = "user-b";
    private const string KeyA = "key-a";
    private const string KeyB = "key-b";
    private const int VersionCharIndex = 14;
    private const int VariantCharIndex = 19;
    private const string VariantChars = "89ab";

    private static readonly Guid DnsNamespace = new("6ba7b810-9dad-11d1-80b4-00c04fd430c8");

    [Theory]
    [InlineData("www.example.com", "2ed6657d-e927-568b-95e1-2665a8aea6a2")]
    [InlineData("python.org", "886313e1-3b8a-5372-9b90-0c9aee199e5d")]
    public void Create_Rfc4122DnsNamespace_MatchesPublishedVectors(string name, string expected)
    {
        var id = DeterministicIds.Create(DnsNamespace, name);

        Assert.Equal(expected, id.ToString());
    }

    [Fact]
    public void BatchId_SameUserAndKey_ReturnsSameId()
    {
        var first = DeterministicIds.BatchId(UserA, KeyA);
        var second = DeterministicIds.BatchId(UserA, KeyA);

        Assert.Equal(first, second);
    }

    [Fact]
    public void BatchId_DifferentUser_ReturnsDifferentId()
    {
        var first = DeterministicIds.BatchId(UserA, KeyA);
        var second = DeterministicIds.BatchId(UserB, KeyA);

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void BatchId_DifferentKey_ReturnsDifferentId()
    {
        var first = DeterministicIds.BatchId(UserA, KeyA);
        var second = DeterministicIds.BatchId(UserA, KeyB);

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void BatchId_ReturnsVersionFiveRfcVariantGuid()
    {
        var id = DeterministicIds.BatchId(UserA, KeyA);

        Assert.Equal('5', id[VersionCharIndex]);
        Assert.Contains(id[VariantCharIndex], VariantChars);
    }

    [Fact]
    public void DocumentId_SameBatchAndClientId_ReturnsSameId()
    {
        var batchId = DeterministicIds.BatchId(UserA, KeyA);
        var clientId = UploadRequests.ClientId(1);

        var first = DeterministicIds.DocumentId(batchId, clientId);
        var second = DeterministicIds.DocumentId(batchId, clientId);

        Assert.Equal(first, second);
    }

    [Fact]
    public void DocumentId_DifferentClientIds_ReturnDifferentIds()
    {
        var batchId = DeterministicIds.BatchId(UserA, KeyA);

        var first = DeterministicIds.DocumentId(batchId, UploadRequests.ClientId(1));
        var second = DeterministicIds.DocumentId(batchId, UploadRequests.ClientId(2));

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void DocumentId_DifferentBatches_ReturnDifferentIds()
    {
        var clientId = UploadRequests.ClientId(1);

        var first = DeterministicIds.DocumentId(DeterministicIds.BatchId(UserA, KeyA), clientId);
        var second = DeterministicIds.DocumentId(DeterministicIds.BatchId(UserA, KeyB), clientId);

        Assert.NotEqual(first, second);
    }
}
