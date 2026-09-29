using Cortexa.JobOrchestrator.Domain.Entities;
using Cortexa.JobOrchestrator.Domain.Enums;
using Cortexa.JobOrchestrator.Domain.StateMachine;
using FluentAssertions;
using Xunit;

namespace Cortexa.JobOrchestrator.Application.Tests;

public sealed class BatchSagaFanOutTests
{
    private static BatchSaga CreateQueuedSaga(string id = "batch-1") =>
        new(id, BatchState.Queued, [], false, false, 0, null, 1);

    private static List<string> DocIds(int count) =>
        Enumerable.Range(1, count).Select(i => $"doc-{i}").ToList();

    [Fact]
    public void MarkActiveTerminal_ForActiveId_RemovesFromActiveSet()
    {
        const int Cap = 3;
        var saga = CreateQueuedSaga();
        saga.SeedFanOut(DocIds(5), Cap);
        var firstActiveId = saga.ActiveDocumentIds.First();

        saga.MarkActiveTerminal(firstActiveId);

        saga.ActiveDocumentIds.Count.Should().Be(2);
        saga.ActiveDocumentIds.Should().NotContain(firstActiveId);
    }

    [Fact]
    public void MarkActiveTerminal_ForNonActiveId_DoesNotThrowAndCountUnchanged()
    {
        const int Cap = 3;
        var saga = CreateQueuedSaga();
        saga.SeedFanOut(DocIds(3), Cap);

        var act = () => saga.MarkActiveTerminal("non-existent-doc");

        act.Should().NotThrow();
        saga.ActiveDocumentIds.Count.Should().Be(3);
    }

    [Fact]
    public void TryReleaseNext_QueueIsEmpty_ReturnsNull()
    {
        const int Cap = 5;
        var saga = CreateQueuedSaga();
        saga.SeedFanOut(DocIds(2), Cap);

        var released = saga.TryReleaseNext();

        released.Should().BeNull();
    }

    [Fact]
    public void TryReleaseNext_QueueHasItems_DequeuesOneAndAddsToActive()
    {
        const int Cap = 2;
        var saga = CreateQueuedSaga();
        saga.SeedFanOut(DocIds(5), Cap);
        var firstQueued = saga.QueuedDocumentIds.Peek();

        var released = saga.TryReleaseNext();

        released.Should().Be(firstQueued);
        saga.ActiveDocumentIds.Count.Should().Be(3);
        saga.QueuedDocumentIds.Count.Should().Be(2);
    }

    [Fact]
    public void TryReleaseNext_ReleasedDoc_IsPresentInDocuments()
    {
        const int Cap = 2;
        var saga = CreateQueuedSaga();
        saga.SeedFanOut(DocIds(5), Cap);
        var firstQueued = saga.QueuedDocumentIds.Peek();

        saga.TryReleaseNext();

        saga.Documents.Should().Contain(d => d.DocumentId == firstQueued);
    }

    [Fact]
    public void MarkActiveTerminal_ThenTryReleaseNext_MovesQueuedToActive()
    {
        const int Cap = 2;
        var saga = CreateQueuedSaga();
        saga.SeedFanOut(DocIds(5), Cap);
        var firstActiveId = saga.ActiveDocumentIds.First();

        saga.MarkActiveTerminal(firstActiveId);
        saga.TryReleaseNext();

        saga.ActiveDocumentIds.Count.Should().Be(2);
        saga.QueuedDocumentIds.Count.Should().Be(2);
    }

    [Fact]
    public void CompletedCount_AfterAdvancingTwoDocsToTerminal_ReturnsTwo()
    {
        const int Cap = 3;
        var saga = CreateQueuedSaga();
        saga.SeedFanOut(DocIds(3), Cap);
        var doc1 = saga.Documents[0];
        var doc2 = saga.Documents[1];

        DocumentTransitions.Advance(doc1, DocumentState.Ingested);
        DocumentTransitions.Advance(doc1, DocumentState.Extracted);
        DocumentTransitions.Advance(doc1, DocumentState.Scored);
        DocumentTransitions.Advance(doc1, DocumentState.Complete);

        DocumentTransitions.Advance(doc2, DocumentState.Failed);

        saga.CompletedCount.Should().Be(2);
    }

    [Fact]
    public void CompletedCount_WithCancelledDocs_ExcludesCancelledFromCount()
    {
        const int Cap = 3;
        var saga = CreateQueuedSaga();
        saga.SeedFanOut(DocIds(3), Cap);
        var doc1 = saga.Documents[0];
        var doc2 = saga.Documents[1];
        var doc3 = saga.Documents[2];

        DocumentTransitions.Advance(doc1, DocumentState.Ingested);
        DocumentTransitions.Advance(doc1, DocumentState.Extracted);
        DocumentTransitions.Advance(doc1, DocumentState.Scored);
        DocumentTransitions.Advance(doc1, DocumentState.Complete);

        DocumentTransitions.Advance(doc2, DocumentState.Cancelled);
        DocumentTransitions.Advance(doc3, DocumentState.Cancelled);

        saga.CompletedCount.Should().Be(1);
    }

    [Fact]
    public void ActiveCount_AfterMutations_MatchesActiveDocumentIdsCount()
    {
        const int Cap = 3;
        var saga = CreateQueuedSaga();
        saga.SeedFanOut(DocIds(5), Cap);
        var firstActiveId = saga.ActiveDocumentIds.First();

        saga.MarkActiveTerminal(firstActiveId);
        saga.TryReleaseNext();

        saga.ActiveCount.Should().Be(saga.ActiveDocumentIds.Count);
    }
}
