using Collector.Server.Application.Handlers;
using Collector.Server.Tests.Fakes;

namespace Collector.Server.Tests.Reads;

public class GetBatchResultsHandlerTests
{
    [Fact]
    public async Task HandleAsync_ForeignBatch_ReturnsNotFoundWithoutReadingReports()
    {
        var store = new FakePipelineRowStore();
        store.Sagas["batch-other"] = TestSagas.Existing("batch-other") with { OwnerUserId = TestIdentity.OtherUserId };
        var handler = new GetBatchResultsHandler(store);

        var outcome = await handler.HandleAsync("batch-other", TestIdentity.Caller, TestContext.Current.CancellationToken);

        Assert.False(outcome.Found);
        Assert.Null(outcome.Results);
        Assert.Equal(0, store.ReportReadCalls);
    }

    [Fact]
    public async Task HandleAsync_OtherOrg_ReturnsNotFound()
    {
        var store = new FakePipelineRowStore();
        store.Sagas["batch-mine"] = TestSagas.Existing("batch-mine") with { OrgId = TestIdentity.OtherOrgId };
        var handler = new GetBatchResultsHandler(store);

        var outcome = await handler.HandleAsync("batch-mine", TestIdentity.Caller, TestContext.Current.CancellationToken);

        Assert.False(outcome.Found);
    }

    [Fact]
    public async Task HandleAsync_MissingBatch_ReturnsNotFound()
    {
        var handler = new GetBatchResultsHandler(new FakePipelineRowStore());

        var outcome = await handler.HandleAsync("nope", TestIdentity.Caller, TestContext.Current.CancellationToken);

        Assert.False(outcome.Found);
    }

    [Fact]
    public async Task HandleAsync_OwnedBatch_ReturnsAssembledCandidates()
    {
        var store = new FakePipelineRowStore();
        store.Sagas[ResultRows.Batch] = TestSagas.Existing(ResultRows.Batch);
        store.HarvestingCandidates.Add(ResultRows.Harvesting("c1", weightedScore: 0.5));
        var handler = new GetBatchResultsHandler(store);

        var outcome = await handler.HandleAsync(ResultRows.Batch, TestIdentity.Caller, TestContext.Current.CancellationToken);

        Assert.True(outcome.Found);
        Assert.Equal(ResultRows.Batch, outcome.Results!.BatchId);
        Assert.Equal("c1", Assert.Single(outcome.Results.Candidates).CandidateId);
    }
}
