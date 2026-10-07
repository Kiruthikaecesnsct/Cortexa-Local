using Collector.Application.Knowledge;
using Collector.Application.Upload;
using Collector.Domain.Upload;
using Collector.Tests.Support;

namespace Collector.Tests.Upload;

public sealed class UploadBatchPlannerTests
{
    private const int FourMebibytes = 4 * 1024 * 1024;
    private const int TenMebibytes = 10 * 1024 * 1024;
    private const int FiftyOneDocuments = 51;

    private readonly UploadBatchPlanner _planner = new();

    private UploadPlan Plan(IReadOnlyList<UploadDocument> documents, string name = "My batch") =>
        _planner.Plan(documents, UploadData.Collector, name);

    private static List<UploadDocument> Documents(int count, int itemsEach = 1) =>
        [.. Enumerable.Range(0, count).Select(index => UploadData.Document($"doc-{index}", itemsEach))];

    [Fact]
    public void Plan_FiftyOneDocuments_SplitsFiftyAndOneWithOwnRequests()
    {
        var plan = Plan(Documents(FiftyOneDocuments));

        Assert.Equal([UploadLimitsMirror.MaxDocumentsPerBatch, 1], plan.Batches.Select(batch => batch.DocumentCount));
        Assert.NotSame(plan.Batches[0].Request, plan.Batches[1].Request);
    }

    [Fact]
    public void Plan_Batches_AreIndexedInOrder()
    {
        var plan = Plan(Documents(FiftyOneDocuments));

        Assert.Equal([0, 1], plan.Batches.Select(batch => batch.Index));
    }

    [Fact]
    public void Plan_NoDocuments_ProducesNothing()
    {
        var plan = Plan([]);

        Assert.Empty(plan.Batches);
        Assert.Empty(plan.Blocked);
    }

    [Fact]
    public void Plan_DocumentOverItemLimit_BlockedWhileOthersUpload()
    {
        var documents = new List<UploadDocument>
        {
            UploadData.Document("small-a"),
            UploadData.Document("huge", UploadLimitsMirror.MaxItemsPerDocument + 1),
            UploadData.Document("small-b"),
        };

        var plan = Plan(documents);

        var blocked = Assert.Single(plan.Blocked);
        Assert.Equal(new BlockedDocument("huge", "huge.cs", UploadLimitsMirror.MaxItemsPerDocument + 1, BlockReason.TooManyItems), blocked);
        Assert.Equal(["small-a", "small-b"], Assert.Single(plan.Batches).Request.Documents.Select(document => document.ClientDocumentId));
    }

    [Fact]
    public void Plan_DocumentWithExactlyMaxItems_IsAllowed()
    {
        var plan = Plan([UploadData.Document("edge", UploadLimitsMirror.MaxItemsPerDocument)]);

        Assert.Empty(plan.Blocked);
        Assert.Equal(UploadLimitsMirror.MaxItemsPerDocument, Assert.Single(plan.Batches).ItemCount);
    }

    [Fact]
    public void Plan_BodyWouldExceedLimit_SplitsBySizeAndEachBodyFits()
    {
        var documents = Enumerable.Range(0, 3).Select(index => UploadData.Document($"big-{index}", 1, FourMebibytes)).ToList();

        var plan = Plan(documents);

        Assert.Equal([2, 1], plan.Batches.Select(batch => batch.DocumentCount));
        Assert.All(plan.Batches, batch => Assert.True(UploadData.BodySize(batch.Request) <= UploadLimitsMirror.MaxBodyBytes));
    }

    [Fact]
    public void Plan_SingleDocumentTooLarge_BlockedAsTooLarge()
    {
        var documents = new List<UploadDocument> { UploadData.Document("giant", 1, TenMebibytes), UploadData.Document("ok") };

        var plan = Plan(documents);

        Assert.Equal(BlockReason.TooLarge, Assert.Single(plan.Blocked).Reason);
        Assert.Equal("ok", Assert.Single(Assert.Single(plan.Batches).Request.Documents).ClientDocumentId);
    }

    [Fact]
    public void Plan_SingleBatch_KeepsBatchNameUnchanged()
    {
        var plan = Plan(Documents(2), "Quarterly upload");

        Assert.Equal("Quarterly upload", Assert.Single(plan.Batches).Request.BatchName);
    }

    [Fact]
    public void Plan_MultipleBatches_AppendNumberedSuffix()
    {
        var plan = Plan(Documents(FiftyOneDocuments), "Quarterly upload");

        Assert.Equal(["Quarterly upload (1 of 2)", "Quarterly upload (2 of 2)"], plan.Batches.Select(batch => batch.Request.BatchName));
    }

    [Fact]
    public void Plan_LongNameWithMultipleBatches_StaysWithinNameLimit()
    {
        var plan = Plan(Documents(FiftyOneDocuments), new string('n', UploadLimitsMirror.BatchNameMax));

        Assert.All(plan.Batches, batch => Assert.True(batch.Request.BatchName.Length <= UploadLimitsMirror.BatchNameMax));
        Assert.EndsWith(" (2 of 2)", plan.Batches[1].Request.BatchName);
    }

    [Fact]
    public void Plan_Batches_CountIncludedItemsPerBatch()
    {
        const int ItemsEach = 3;

        var plan = Plan(Documents(FiftyOneDocuments, ItemsEach));

        Assert.Equal([UploadLimitsMirror.MaxDocumentsPerBatch * ItemsEach, ItemsEach], plan.Batches.Select(batch => batch.ItemCount));
    }

    [Fact]
    public void Plan_Batches_CarryCollectorInfo()
    {
        var plan = Plan(Documents(1));

        Assert.Equal(UploadData.Collector, Assert.Single(plan.Batches).Request.Collector);
    }
}
