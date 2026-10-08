using Collector.Server.Application.Upload;

namespace Collector.Server.Tests.Upload;

public class RequestFingerprintTests
{
    private const int Sha256HexLength = 64;
    private const string ChangedTitle = "Changed Title";
    private const int OtherDocumentNumber = 9;

    [Fact]
    public void Compute_SameRequestTwice_ReturnsSameHash()
    {
        var request = UploadRequests.Valid(UploadRequests.PaperDocument(), UploadRequests.CodeDocument());

        var first = RequestFingerprint.Compute(request);
        var second = RequestFingerprint.Compute(request);

        Assert.Equal(first, second);
        Assert.Equal(Sha256HexLength, first.Length);
    }

    [Fact]
    public void Compute_ChangedItem_ReturnsDifferentHash()
    {
        var original = UploadRequests.Valid(UploadRequests.PaperDocument());
        var changedItem = UploadRequests.PaperSection() with { Title = ChangedTitle };
        var changed = UploadRequests.Valid(UploadRequests.PaperDocument(changedItem));

        Assert.NotEqual(RequestFingerprint.Compute(original), RequestFingerprint.Compute(changed));
    }

    [Fact]
    public void Compute_ChangedClientDocumentId_ReturnsDifferentHash()
    {
        var original = UploadRequests.Valid(UploadRequests.PaperDocument());
        var renamed = UploadRequests.PaperDocument() with { ClientDocumentId = UploadRequests.ClientId(OtherDocumentNumber).ToString() };
        var changed = UploadRequests.Valid(renamed);

        Assert.NotEqual(RequestFingerprint.Compute(original), RequestFingerprint.Compute(changed));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Matches_StoredFingerprintMissing_ReturnsTrue(string? stored)
    {
        var computed = RequestFingerprint.Compute(UploadRequests.Valid());

        Assert.True(RequestFingerprint.Matches(stored, computed));
    }

    [Fact]
    public void Matches_StoredFingerprintDiffers_ReturnsFalse()
    {
        var computed = RequestFingerprint.Compute(UploadRequests.Valid());
        var other = RequestFingerprint.Compute(UploadRequests.Valid(UploadRequests.CodeDocument()));

        Assert.False(RequestFingerprint.Matches(other, computed));
    }
}
