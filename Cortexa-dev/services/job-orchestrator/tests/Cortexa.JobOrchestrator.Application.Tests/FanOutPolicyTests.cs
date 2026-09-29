using Cortexa.JobOrchestrator.Domain.Entities;
using Cortexa.JobOrchestrator.Domain.Enums;
using FluentAssertions;
using Xunit;

namespace Cortexa.JobOrchestrator.Application.Tests;

public sealed class FanOutPolicyTests
{
    private const int DefaultCap = 3;

    private static BatchSaga CreateQueuedSaga(string id = "batch-1") =>
        new(id, BatchState.Queued, [], false, false, 0, null, 1);

    private static List<string> DocIds(int count) =>
        Enumerable.Range(1, count).Select(i => $"doc-{i}").ToList();

    [Fact]
    public void SeedFanOut_CapSmallerThanDocCount_SplitsActiveAndQueuedCorrectly()
    {
        const int DocCount = 10;
        const int Cap = 3;
        var saga = CreateQueuedSaga();

        saga.SeedFanOut(DocIds(DocCount), Cap);

        saga.ActiveDocumentIds.Count.Should().Be(3);
        saga.QueuedDocumentIds.Count.Should().Be(7);
    }

    [Fact]
    public void SeedFanOut_CapLargerThanDocCount_AllDocsAreActive()
    {
        const int DocCount = 5;
        const int Cap = 10;
        var saga = CreateQueuedSaga();

        saga.SeedFanOut(DocIds(DocCount), Cap);

        saga.ActiveDocumentIds.Count.Should().Be(5);
        saga.QueuedDocumentIds.Count.Should().Be(0);
    }

    [Fact]
    public void SeedFanOut_CapEqualsDocCount_AllDocsAreActive()
    {
        const int DocCount = 5;
        const int Cap = 5;
        var saga = CreateQueuedSaga();

        saga.SeedFanOut(DocIds(DocCount), Cap);

        saga.ActiveDocumentIds.Count.Should().Be(5);
        saga.QueuedDocumentIds.Count.Should().Be(0);
    }

    [Fact]
    public void SeedFanOut_CapOfOne_OnlyOneDocIsActive()
    {
        const int DocCount = 4;
        const int Cap = 1;
        var saga = CreateQueuedSaga();

        saga.SeedFanOut(DocIds(DocCount), Cap);

        saga.ActiveDocumentIds.Count.Should().Be(1);
        saga.QueuedDocumentIds.Count.Should().Be(3);
    }

    [Fact]
    public void SeedFanOut_EmptyDocList_ProducesZeroActiveAndZeroQueued()
    {
        var saga = CreateQueuedSaga();

        saga.SeedFanOut([], DefaultCap);

        saga.ActiveDocumentIds.Count.Should().Be(0);
        saga.QueuedDocumentIds.Count.Should().Be(0);
    }

    [Fact]
    public void SeedFanOut_CalledTwiceWithSameList_SecondCallIsNoOp()
    {
        const int DocCount = 5;
        var saga = CreateQueuedSaga();

        saga.SeedFanOut(DocIds(DocCount), DefaultCap);
        var activeAfterFirst = saga.ActiveDocumentIds.Count;
        var queuedAfterFirst = saga.QueuedDocumentIds.Count;

        saga.SeedFanOut(DocIds(DocCount), DefaultCap);

        saga.ActiveDocumentIds.Count.Should().Be(activeAfterFirst);
        saga.QueuedDocumentIds.Count.Should().Be(queuedAfterFirst);
    }

    [Fact]
    public void SeedFanOut_StateWasQueued_TransitionsToInProgress()
    {
        var saga = CreateQueuedSaga();

        saga.SeedFanOut(DocIds(3), DefaultCap);

        saga.State.Should().Be(BatchState.InProgress);
    }

    [Fact]
    public void SeedFanOut_ActiveDocIds_AllPresentInDocumentsWithQueuedState()
    {
        const int Cap = 3;
        var ids = DocIds(5);
        var saga = CreateQueuedSaga();

        saga.SeedFanOut(ids, Cap);

        var activeIds = saga.ActiveDocumentIds;
        foreach (var id in activeIds)
        {
            saga.Documents.Should().Contain(d => d.DocumentId == id && d.State == DocumentState.Queued);
        }
    }
}
