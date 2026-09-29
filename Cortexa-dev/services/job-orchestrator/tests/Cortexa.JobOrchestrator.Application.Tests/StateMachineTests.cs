using Cortexa.JobOrchestrator.Domain.Entities;
using Cortexa.JobOrchestrator.Domain.Enums;
using Cortexa.JobOrchestrator.Domain.Errors;
using Cortexa.JobOrchestrator.Domain.StateMachine;
using FluentAssertions;
using Xunit;

namespace Cortexa.JobOrchestrator.Application.Tests;

public sealed class DocumentTransitionTests
{
    [Fact]
    public void CanAdvance_ValidTransition_ReturnsTrue()
    {
        var result = DocumentTransitions.CanAdvance(DocumentState.Queued, DocumentState.Ingested);

        result.Should().BeTrue();
    }

    [Fact]
    public void CanAdvance_InvalidTransition_ReturnsFalse()
    {
        var result = DocumentTransitions.CanAdvance(DocumentState.Ingested, DocumentState.Scored);

        result.Should().BeFalse();
    }

    [Fact]
    public void CanAdvance_TerminalState_ReturnsFalse()
    {
        var result = DocumentTransitions.CanAdvance(DocumentState.Complete, DocumentState.Ingested);

        result.Should().BeFalse();
    }

    [Fact]
    public void Advance_ValidTransition_UpdatesState()
    {
        var document = new DocumentProgress("doc-1", DocumentState.Queued);

        DocumentTransitions.Advance(document, DocumentState.Ingested);

        document.State.Should().Be(DocumentState.Ingested);
    }

    [Fact]
    public void Advance_InvalidTransition_ThrowsInvalidTransitionException()
    {
        var document = new DocumentProgress("doc-1", DocumentState.Ingested);

        var act = () => DocumentTransitions.Advance(document, DocumentState.Scored);

        act.Should().Throw<InvalidTransitionException>()
            .Which.FromState.Should().Be(DocumentState.Ingested.ToString());
    }

    [Fact]
    public void Advance_InvalidTransition_ExceptionMessageContainsValidTransitions()
    {
        var document = new DocumentProgress("doc-1", DocumentState.Ingested);

        var act = () => DocumentTransitions.Advance(document, DocumentState.Scored);

        act.Should().Throw<InvalidTransitionException>()
            .WithMessage("*Extracted*");
    }

    [Theory]
    [InlineData(DocumentState.Queued, DocumentState.Failed)]
    [InlineData(DocumentState.Ingested, DocumentState.Failed)]
    [InlineData(DocumentState.Extracted, DocumentState.Failed)]
    [InlineData(DocumentState.Scored, DocumentState.Failed)]
    public void Advance_AnyActiveState_CanTransitionToFailed(DocumentState from, DocumentState to)
    {
        var document = new DocumentProgress("doc-1", from);

        DocumentTransitions.Advance(document, to);

        document.State.Should().Be(to);
    }

    [Theory]
    [InlineData(DocumentState.Scored, DocumentState.Harvested)]
    [InlineData(DocumentState.Scored, DocumentState.Seeded)]
    [InlineData(DocumentState.Harvested, DocumentState.Seeded)]
    [InlineData(DocumentState.Harvested, DocumentState.Complete)]
    [InlineData(DocumentState.Seeded, DocumentState.Complete)]
    public void Advance_HarvestingAndSeedingPaths_ValidTransitions(DocumentState from, DocumentState to)
    {
        var document = new DocumentProgress("doc-1", from);

        DocumentTransitions.Advance(document, to);

        document.State.Should().Be(to);
    }

    [Fact]
    public void CanAdvance_IngestedToNoCandidates_ReturnsTrue()
    {
        var result = DocumentTransitions.CanAdvance(DocumentState.Ingested, DocumentState.NoCandidates);

        result.Should().BeTrue();
    }

    [Fact]
    public void Advance_IngestedToNoCandidates_UpdatesState()
    {
        var document = new DocumentProgress("doc-1", DocumentState.Ingested);

        DocumentTransitions.Advance(document, DocumentState.NoCandidates);

        document.State.Should().Be(DocumentState.NoCandidates);
    }

    [Fact]
    public void DocumentTransitions_NoCandidatesState_HasNoValidTransitions()
    {
        var transitions = DocumentTransitions.ValidTransitions(DocumentState.NoCandidates);

        transitions.Should().BeEmpty();
    }

    [Fact]
    public void CanAdvance_NoCandidatesToAnyState_ReturnsFalse()
    {
        var result = DocumentTransitions.CanAdvance(DocumentState.NoCandidates, DocumentState.Extracted);

        result.Should().BeFalse();
    }

    [Fact]
    public void DocumentProgress_NoCandidatesState_IsTerminal()
    {
        var doc = new DocumentProgress("doc-1", DocumentState.NoCandidates);

        doc.IsTerminal.Should().BeTrue();
    }
}

public sealed class BatchRollUpTests
{
    [Fact]
    public void DeriveFromDocuments_AllQueued_ReturnsBatchQueued()
    {
        var docs = new List<DocumentProgress>
        {
            new("doc-1", DocumentState.Queued),
            new("doc-2", DocumentState.Queued)
        };

        var result = BatchTransitions.DeriveFromDocuments(docs);

        result.Should().Be(BatchState.Queued);
    }

    [Fact]
    public void DeriveFromDocuments_AnyInProgress_ReturnsBatchInProgress()
    {
        var docs = new List<DocumentProgress>
        {
            new("doc-1", DocumentState.Ingested),
            new("doc-2", DocumentState.Queued)
        };

        var result = BatchTransitions.DeriveFromDocuments(docs);

        result.Should().Be(BatchState.InProgress);
    }

    [Fact]
    public void DeriveFromDocuments_AllComplete_ReturnsBatchCompleted()
    {
        var docs = new List<DocumentProgress>
        {
            new("doc-1", DocumentState.Complete),
            new("doc-2", DocumentState.Complete)
        };

        var result = BatchTransitions.DeriveFromDocuments(docs);

        result.Should().Be(BatchState.Completed);
    }

    [Fact]
    public void DeriveFromDocuments_AllFailed_ReturnsBatchFailed()
    {
        var docs = new List<DocumentProgress>
        {
            new("doc-1", DocumentState.Failed),
            new("doc-2", DocumentState.Failed)
        };

        var result = BatchTransitions.DeriveFromDocuments(docs);

        result.Should().Be(BatchState.Failed);
    }

    [Fact]
    public void DeriveFromDocuments_MixOfCompleteAndFailed_ReturnsBatchCompleted()
    {
        var docs = new List<DocumentProgress>
        {
            new("doc-1", DocumentState.Complete),
            new("doc-2", DocumentState.Failed)
        };

        var result = BatchTransitions.DeriveFromDocuments(docs);

        result.Should().Be(BatchState.Completed);
    }

    [Fact]
    public void DeriveFromDocuments_EmptyList_ReturnsBatchQueued()
    {
        var result = BatchTransitions.DeriveFromDocuments([]);

        result.Should().Be(BatchState.Queued);
    }

    [Fact]
    public void DeriveFromDocuments_SingleDocumentNoCandidates_ReturnsBatchCompleted()
    {
        var docs = new List<DocumentProgress>
        {
            new("doc-1", DocumentState.NoCandidates)
        };

        var result = BatchTransitions.DeriveFromDocuments(docs);

        result.Should().Be(BatchState.Completed);
    }

    [Fact]
    public void DeriveFromDocuments_MixOfCompleteAndNoCandidates_ReturnsBatchCompleted()
    {
        var docs = new List<DocumentProgress>
        {
            new("doc-1", DocumentState.Complete),
            new("doc-2", DocumentState.NoCandidates)
        };

        var result = BatchTransitions.DeriveFromDocuments(docs);

        result.Should().Be(BatchState.Completed);
    }

    [Fact]
    public void DeriveFromDocuments_AllNoCandidates_ReturnsBatchCompleted()
    {
        var docs = new List<DocumentProgress>
        {
            new("doc-1", DocumentState.NoCandidates),
            new("doc-2", DocumentState.NoCandidates)
        };

        var result = BatchTransitions.DeriveFromDocuments(docs);

        result.Should().Be(BatchState.Completed);
    }
}

public sealed class CancelledTransitionTests
{
    [Theory]
    [InlineData(DocumentState.Queued)]
    [InlineData(DocumentState.Ingested)]
    [InlineData(DocumentState.Extracted)]
    [InlineData(DocumentState.Scored)]
    [InlineData(DocumentState.Harvested)]
    [InlineData(DocumentState.Seeded)]
    public void DocumentTransitions_AnyNonTerminalState_CanTransitionToCancelled(DocumentState from)
    {
        var result = DocumentTransitions.CanAdvance(from, DocumentState.Cancelled);

        result.Should().BeTrue();
    }

    [Theory]
    [InlineData(DocumentState.Complete)]
    [InlineData(DocumentState.Failed)]
    [InlineData(DocumentState.Cancelled)]
    public void DocumentTransitions_TerminalState_CannotTransitionToCancelled(DocumentState from)
    {
        var result = DocumentTransitions.CanAdvance(from, DocumentState.Cancelled);

        result.Should().BeFalse();
    }

    [Fact]
    public void DocumentTransitions_CancelledState_HasNoValidTransitions()
    {
        var transitions = DocumentTransitions.ValidTransitions(DocumentState.Cancelled);

        transitions.Should().BeEmpty();
    }

    [Fact]
    public void DocumentProgress_CancelledState_IsTerminal()
    {
        var doc = new DocumentProgress("doc-1", DocumentState.Cancelled);

        doc.IsTerminal.Should().BeTrue();
    }

    [Theory]
    [InlineData(BatchState.Queued)]
    [InlineData(BatchState.InProgress)]
    public void BatchTransitions_QueuedOrInProgress_CanTransitionToCancelled(BatchState from)
    {
        var result = BatchTransitions.CanAdvance(from, BatchState.Cancelled);

        result.Should().BeTrue();
    }

    [Theory]
    [InlineData(BatchState.Completed)]
    [InlineData(BatchState.Failed)]
    [InlineData(BatchState.Cancelled)]
    public void BatchTransitions_TerminalState_CannotTransitionToCancelled(BatchState from)
    {
        var result = BatchTransitions.CanAdvance(from, BatchState.Cancelled);

        result.Should().BeFalse();
    }

    [Fact]
    public void DeriveFromDocuments_CurrentStateIsCancelled_ReturnsCancelledUnchanged()
    {
        var docs = new List<DocumentProgress>
        {
            new("doc-1", DocumentState.Ingested),
            new("doc-2", DocumentState.Complete)
        };

        var result = BatchTransitions.DeriveFromDocuments(docs, BatchState.Cancelled);

        result.Should().Be(BatchState.Cancelled);
    }

    [Fact]
    public void DeriveFromDocuments_NoCurrentState_DrivesFromDocumentsNormally()
    {
        var docs = new List<DocumentProgress>
        {
            new("doc-1", DocumentState.Complete),
            new("doc-2", DocumentState.Complete)
        };

        var result = BatchTransitions.DeriveFromDocuments(docs);

        result.Should().Be(BatchState.Completed);
    }
}
