using System.Text.Json;
using Cortexa.JobOrchestrator.Application.Contracts;
using Cortexa.JobOrchestrator.Application.Exceptions;
using Cortexa.JobOrchestrator.Application.Handlers;
using Cortexa.JobOrchestrator.Application.Interfaces;
using Cortexa.JobOrchestrator.Domain.Entities;
using Cortexa.JobOrchestrator.Domain.Enums;
using Cortexa.JobOrchestrator.Domain.Errors;
using Cortexa.JobOrchestrator.Application.Settings;
using Cortexa.JobOrchestrator.Infrastructure.Configuration;
using Cortexa.JobOrchestrator.Infrastructure.Messaging;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace Cortexa.JobOrchestrator.Application.Tests;

public sealed class SagaMessageProcessorTests
{
    private readonly ISagaRepository _repository;
    private readonly IEventPublisher _publisher;
    private readonly IRetryPolicy _retryPolicy;
    private readonly IMessageActions _actions;
    private readonly SagaMessageProcessor _processor;

    public SagaMessageProcessorTests()
    {
        _repository = Substitute.For<ISagaRepository>();
        _publisher = Substitute.For<IEventPublisher>();
        _retryPolicy = Substitute.For<IRetryPolicy>();
        _actions = Substitute.For<IMessageActions>();

        _retryPolicy.MaxRetries.Returns(3);

        var handler = new AdvanceSagaHandler(_repository, _publisher, NullLogger<AdvanceSagaHandler>.Instance);
        var fanOutHandler = new CreateBatchFanOutHandler(
            _repository,
            _publisher,
            Options.Create(new OrchestratorSettings()));
        _processor = new SagaMessageProcessor(
            handler,
            fanOutHandler,
            _repository,
            _retryPolicy,
            Options.Create(new ServiceBusSettings { ConcurrencyConflictBackoffMilliseconds = 0 }),
            NullLogger<SagaMessageProcessor>.Instance);
    }

    private static string SerializeEnvelope(
        string batchId = "batch-1",
        string eventType = "ingestion.completed",
        string documentId = "doc-1",
        Dictionary<string, object>? payload = null)
    {
        var envelope = new EventEnvelope
        {
            EventType = eventType,
            BatchId = batchId,
            DocumentId = documentId,
            Payload = payload ?? []
        };
        return JsonSerializer.Serialize(envelope);
    }

    private static BatchSaga CreateSaga(string batchId = "batch-1", DocumentState docState = DocumentState.Queued)
    {
        var doc = new DocumentProgress("doc-1", docState);
        return new BatchSaga(batchId, BatchState.InProgress, [doc], true, false, 1, "\"etag\"", 1);
    }

    private static BatchSaga CreateScoringInProgressSaga(
        string batchId,
        int expectedCandidateCount,
        IEnumerable<string> alreadyCompletedCandidateIds,
        bool wantsHarvesting = true,
        bool wantsSeeding = false)
    {
        var doc = new DocumentProgress("doc-1", DocumentState.Extracted);
        doc.SetExpectedCandidates(expectedCandidateCount);
        foreach (var candidateId in alreadyCompletedCandidateIds)
            doc.MarkCandidateScored(candidateId);

        return new BatchSaga(batchId, BatchState.InProgress, [doc], wantsHarvesting, wantsSeeding, 1, "\"etag\"", 1);
    }

    [Fact]
    public async Task ProcessAsync_NullEnvelope_DeadLettersWithDeserializationFailed()
    {
        await _processor.ProcessAsync("null", _actions, CancellationToken.None);

        await _actions.Received(1).DeadLetterAsync("DeserializationFailed", Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _actions.DidNotReceive().CompleteAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessAsync_PermanentError_InvalidTransition_DeadLetters()
    {
        var saga = CreateSaga(docState: DocumentState.Failed);
        saga.Documents.First().SetExpectedCandidates(1);
        _repository.GetAsync("batch-1", Arg.Any<CancellationToken>()).Returns(saga);

        var payload = new Dictionary<string, object> { [PayloadKeys.CandidateId] = "cand-1" };
        var body = SerializeEnvelope(eventType: "scoring.completed", payload: payload);

        await _processor.ProcessAsync(body, _actions, CancellationToken.None);

        await _actions.Received(1).DeadLetterAsync("InvalidTransition", Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _actions.DidNotReceive().CompleteAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessAsync_PermanentError_SagaNotFound_DeadLetters()
    {
        _repository.GetAsync("batch-1", Arg.Any<CancellationToken>()).Returns(Task.FromResult<BatchSaga?>(null));

        var body = SerializeEnvelope();

        await _processor.ProcessAsync(body, _actions, CancellationToken.None);

        await _actions.Received(1).DeadLetterAsync("InvalidMessage", Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _actions.DidNotReceive().CompleteAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessAsync_ConcurrencyConflict_PersistsAcrossRetries_ExhaustsAttemptsThenAbandons()
    {
        _repository.GetAsync("batch-1", Arg.Any<CancellationToken>()).Returns(_ => CreateSaga());
        _repository.UpdateAsync(Arg.Any<BatchSaga>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new ConcurrencyConflictException("batch-1"));

        var body = SerializeEnvelope();

        await _processor.ProcessAsync(body, _actions, CancellationToken.None);

        await _repository.Received(3).GetAsync("batch-1", Arg.Any<CancellationToken>());
        await _actions.Received(1).AbandonAsync(Arg.Any<CancellationToken>());
        await _actions.DidNotReceive().CompleteAsync(Arg.Any<CancellationToken>());
        await _actions.DidNotReceive().DeadLetterAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessAsync_ConcurrencyConflict_ReloadsAndSucceedsOnRetry_CompletesWithoutAbandon()
    {
        var callCount = 0;
        _repository.GetAsync("batch-1", Arg.Any<CancellationToken>()).Returns(_ => CreateSaga());
        _repository.UpdateAsync(Arg.Any<BatchSaga>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                callCount++;
                if (callCount == 1)
                    throw new ConcurrencyConflictException("batch-1");
                return Task.CompletedTask;
            });

        var body = SerializeEnvelope();

        await _processor.ProcessAsync(body, _actions, CancellationToken.None);

        await _repository.Received(2).UpdateAsync(Arg.Any<BatchSaga>(), Arg.Any<CancellationToken>());
        await _actions.Received(1).CompleteAsync(Arg.Any<CancellationToken>());
        await _actions.DidNotReceive().AbandonAsync(Arg.Any<CancellationToken>());
        await _actions.DidNotReceive().DeadLetterAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessAsync_ConcurrencyConflict_RetrySucceeds_NotAllCandidatesResolved_NeverPublishes()
    {
        const int ExpectedCandidateCount = 2;
        var updateCallCount = 0;
        _repository.GetAsync("batch-1", Arg.Any<CancellationToken>())
            .Returns(_ => CreateScoringInProgressSaga("batch-1", ExpectedCandidateCount, []));
        _repository.UpdateAsync(Arg.Any<BatchSaga>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                updateCallCount++;
                if (updateCallCount == 1)
                    throw new ConcurrencyConflictException("batch-1");
                return Task.CompletedTask;
            });

        var payload = new Dictionary<string, object> { [PayloadKeys.CandidateId] = "cand-1" };
        var body = SerializeEnvelope(eventType: "scoring.completed", payload: payload);

        await _processor.ProcessAsync(body, _actions, CancellationToken.None);

        await _repository.Received(2).UpdateAsync(Arg.Any<BatchSaga>(), Arg.Any<CancellationToken>());
        await _publisher.DidNotReceive().PublishAsync(Arg.Any<EventEnvelope>(), Arg.Any<CancellationToken>());
        await _actions.Received(1).CompleteAsync(Arg.Any<CancellationToken>());
        await _actions.DidNotReceive().AbandonAsync(Arg.Any<CancellationToken>());
        await _actions.DidNotReceive().DeadLetterAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessAsync_ConcurrencyConflict_RetrySucceeds_AllCandidatesResolved_PublishesDownstreamEnvelopeExactlyOnce()
    {
        const int ExpectedCandidateCount = 2;
        var updateCallCount = 0;
        _repository.GetAsync("batch-1", Arg.Any<CancellationToken>())
            .Returns(_ => CreateScoringInProgressSaga("batch-1", ExpectedCandidateCount, ["cand-a"]));
        _repository.UpdateAsync(Arg.Any<BatchSaga>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                updateCallCount++;
                if (updateCallCount == 1)
                    throw new ConcurrencyConflictException("batch-1");
                return Task.CompletedTask;
            });

        var payload = new Dictionary<string, object> { [PayloadKeys.CandidateId] = "cand-b" };
        var body = SerializeEnvelope(eventType: "scoring.completed", payload: payload);

        await _processor.ProcessAsync(body, _actions, CancellationToken.None);

        await _repository.Received(2).UpdateAsync(Arg.Any<BatchSaga>(), Arg.Any<CancellationToken>());
        await _publisher.Received(1).PublishAsync(
            Arg.Is<EventEnvelope>(e => e.EventType == SagaEventType.HarvestingRequested),
            Arg.Any<CancellationToken>());
        await _actions.Received(1).CompleteAsync(Arg.Any<CancellationToken>());
        await _actions.DidNotReceive().AbandonAsync(Arg.Any<CancellationToken>());
        await _actions.DidNotReceive().DeadLetterAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessAsync_TransientError_Attempt1_SchedulesRetryWith1SecondDelay()
    {
        _repository.GetAsync("batch-1", Arg.Any<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("transient"));
        _actions.RetryAttempt.Returns(1);
        _retryPolicy.ShouldRetry(1).Returns(true);
        _retryPolicy.DelayFor(1).Returns(TimeSpan.FromSeconds(1));

        var body = SerializeEnvelope();

        await _processor.ProcessAsync(body, _actions, CancellationToken.None);

        await _actions.Received(1).ScheduleRetryAsync(body, 2, TimeSpan.FromSeconds(1), Arg.Any<CancellationToken>());
        await _actions.Received(1).CompleteAsync(Arg.Any<CancellationToken>());
        await _actions.DidNotReceive().DeadLetterAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessAsync_TransientError_Attempt2_SchedulesRetryWith2SecondDelay()
    {
        _repository.GetAsync("batch-1", Arg.Any<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("transient"));
        _actions.RetryAttempt.Returns(2);
        _retryPolicy.ShouldRetry(2).Returns(true);
        _retryPolicy.DelayFor(2).Returns(TimeSpan.FromSeconds(2));

        var body = SerializeEnvelope();

        await _processor.ProcessAsync(body, _actions, CancellationToken.None);

        await _actions.Received(1).ScheduleRetryAsync(body, 3, TimeSpan.FromSeconds(2), Arg.Any<CancellationToken>());
        await _actions.Received(1).CompleteAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessAsync_TransientError_Attempt3_SchedulesRetryWith4SecondDelay()
    {
        _repository.GetAsync("batch-1", Arg.Any<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("transient"));
        _actions.RetryAttempt.Returns(3);
        _retryPolicy.ShouldRetry(3).Returns(true);
        _retryPolicy.DelayFor(3).Returns(TimeSpan.FromSeconds(4));

        var body = SerializeEnvelope();

        await _processor.ProcessAsync(body, _actions, CancellationToken.None);

        await _actions.Received(1).ScheduleRetryAsync(body, 4, TimeSpan.FromSeconds(4), Arg.Any<CancellationToken>());
        await _actions.Received(1).CompleteAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessAsync_TransientError_RetriesExhausted_PersistsFailureAndDeadLetters()
    {
        var saga = CreateSaga();
        var callCount = 0;
        _repository.GetAsync("batch-1", Arg.Any<CancellationToken>()).Returns(_ =>
        {
            callCount++;
            if (callCount == 1) throw new HttpRequestException("transient");
            return Task.FromResult<BatchSaga?>(saga);
        });
        _actions.RetryAttempt.Returns(4);
        _retryPolicy.ShouldRetry(4).Returns(false);

        var body = SerializeEnvelope();

        await _processor.ProcessAsync(body, _actions, CancellationToken.None);

        await _repository.Received(1).UpdateAsync(
            Arg.Is<BatchSaga>(s => s.State == BatchState.Failed),
            Arg.Any<CancellationToken>());
        await _actions.Received(1).DeadLetterAsync("RetriesExhausted", Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _actions.DidNotReceive().CompleteAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessAsync_RetriesExhausted_BatchNotFound_StillDeadLetters()
    {
        var callCount = 0;
        _repository.GetAsync("batch-1", Arg.Any<CancellationToken>()).Returns(_ =>
        {
            callCount++;
            if (callCount == 1) throw new HttpRequestException("transient");
            return Task.FromResult<BatchSaga?>(null);
        });
        _actions.RetryAttempt.Returns(4);
        _retryPolicy.ShouldRetry(4).Returns(false);

        var body = SerializeEnvelope();

        await _processor.ProcessAsync(body, _actions, CancellationToken.None);

        await _repository.DidNotReceive().UpdateAsync(Arg.Any<BatchSaga>(), Arg.Any<CancellationToken>());
        await _actions.Received(1).DeadLetterAsync("RetriesExhausted", Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(BatchState.Cancelled)]
    [InlineData(BatchState.Completed)]
    [InlineData(BatchState.Failed)]
    public async Task ProcessAsync_RetriesExhausted_AlreadyTerminalBatch_SkipsMarkFailedAndDeadLetters(BatchState terminalState)
    {
        var doc = new DocumentProgress("doc-1", DocumentState.Queued);
        var saga = new BatchSaga("batch-1", terminalState, [doc], true, false, 1, "\"etag\"", 1);
        var callCount = 0;
        _repository.GetAsync("batch-1", Arg.Any<CancellationToken>()).Returns(_ =>
        {
            callCount++;
            if (callCount == 1) throw new HttpRequestException("transient");
            return Task.FromResult<BatchSaga?>(saga);
        });
        _actions.RetryAttempt.Returns(4);
        _retryPolicy.ShouldRetry(4).Returns(false);

        var body = SerializeEnvelope();

        await _processor.ProcessAsync(body, _actions, CancellationToken.None);

        await _repository.DidNotReceive().UpdateAsync(Arg.Any<BatchSaga>(), Arg.Any<CancellationToken>());
        await _actions.Received(1).DeadLetterAsync("RetriesExhausted", Arg.Any<string>(), Arg.Any<CancellationToken>());
        saga.State.Should().Be(terminalState);
    }
}
