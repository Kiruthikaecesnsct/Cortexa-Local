using System.Text.Json;
using Cortexa.JobOrchestrator.Application.Contracts;
using Cortexa.JobOrchestrator.Application.Exceptions;
using Cortexa.JobOrchestrator.Application.Handlers;
using Cortexa.JobOrchestrator.Application.Interfaces;
using Cortexa.JobOrchestrator.Domain.Enums;
using Cortexa.JobOrchestrator.Domain.Errors;
using Cortexa.JobOrchestrator.Infrastructure.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Cortexa.JobOrchestrator.Infrastructure.Messaging;

public sealed class SagaMessageProcessor
{
    private readonly AdvanceSagaHandler _handler;
    private readonly CreateBatchFanOutHandler _fanOutHandler;
    private readonly ISagaRepository _repository;
    private readonly IRetryPolicy _retryPolicy;
    private readonly ILogger<SagaMessageProcessor> _logger;
    private readonly int _concurrencyConflictMaxAttempts;
    private readonly TimeSpan _concurrencyConflictBackoff;

    public SagaMessageProcessor(
        AdvanceSagaHandler handler,
        CreateBatchFanOutHandler fanOutHandler,
        ISagaRepository repository,
        IRetryPolicy retryPolicy,
        IOptions<ServiceBusSettings> serviceBusSettings,
        ILogger<SagaMessageProcessor> logger)
    {
        _handler = handler;
        _fanOutHandler = fanOutHandler;
        _repository = repository;
        _retryPolicy = retryPolicy;
        _logger = logger;

        var settings = serviceBusSettings.Value;
        _concurrencyConflictMaxAttempts = Math.Max(1, settings.ConcurrencyConflictMaxAttempts);
        _concurrencyConflictBackoff = TimeSpan.FromMilliseconds(settings.ConcurrencyConflictBackoffMilliseconds);
    }

    public async Task ProcessAsync(string messageBody, IMessageActions actions, CancellationToken ct)
    {
        EventEnvelope? envelope = null;
        try
        {
            envelope = JsonSerializer.Deserialize<EventEnvelope>(messageBody);
            if (envelope is null)
            {
                await actions.DeadLetterAsync("DeserializationFailed", "Envelope was null after deserialization.", ct);
                return;
            }

            using var scope = _logger.BeginScope(new Dictionary<string, object>
            {
                ["batch_id"] = envelope.BatchId,
                ["correlation_id"] = string.IsNullOrEmpty(envelope.CorrelationId) ? envelope.BatchId : envelope.CorrelationId
            });

            _logger.LogInformation("Message received: {EventType}", envelope.EventType);

            if (envelope.EventType.Equals(SagaEventType.BatchCreated, StringComparison.OrdinalIgnoreCase))
                await _fanOutHandler.HandleAsync(envelope, ct);
            else
                await ExecuteWithConcurrencyRetryAsync(() => _handler.HandleAsync(envelope, ct), envelope.BatchId, ct);

            await actions.CompleteAsync(ct);

            _logger.LogInformation("Message processed successfully: {EventType}", envelope.EventType);
        }
        catch (Exception ex) when (IsPermanentError(ex))
        {
            _logger.LogError(ex, "Permanent error for batch {BatchId}. Dead-lettering.", envelope?.BatchId);
            await actions.DeadLetterAsync(GetReason(ex), ex.Message, ct);
        }
        catch (ConcurrencyConflictException ex)
        {
            _logger.LogWarning(ex, "Concurrency conflict for batch {BatchId} could not be resolved. Abandoning.", envelope?.BatchId);
            await actions.AbandonAsync(ct);
        }
        catch (Exception ex)
        {
            await HandleTransientErrorAsync(ex, messageBody, envelope?.BatchId, actions, ct);
        }
    }

    private async Task ExecuteWithConcurrencyRetryAsync(Func<Task> action, string batchId, CancellationToken ct)
    {
        for (var attempt = 1; attempt <= _concurrencyConflictMaxAttempts; attempt++)
        {
            try
            {
                await action();
                return;
            }
            catch (ConcurrencyConflictException ex) when (attempt < _concurrencyConflictMaxAttempts)
            {
                _logger.LogWarning(
                    ex,
                    "Concurrency conflict for batch {BatchId} on attempt {Attempt} of {MaxAttempts}. Reloading and retrying.",
                    batchId,
                    attempt,
                    _concurrencyConflictMaxAttempts);
                await Task.Delay(_concurrencyConflictBackoff, ct);
            }
        }
    }

    private async Task HandleTransientErrorAsync(
        Exception ex,
        string messageBody,
        string? batchId,
        IMessageActions actions,
        CancellationToken ct)
    {
        var attempt = actions.RetryAttempt;
        if (_retryPolicy.ShouldRetry(attempt))
        {
            var delay = _retryPolicy.DelayFor(attempt);
            _logger.LogWarning(ex, "Transient error for batch {BatchId}, attempt {Attempt}. Retrying in {Delay}.", batchId, attempt, delay);
            await actions.ScheduleRetryAsync(messageBody, attempt + 1, delay, ct);
            await actions.CompleteAsync(ct);
            return;
        }

        _logger.LogError(ex, "Transient error for batch {BatchId} exhausted retries. Persisting failure and dead-lettering.", batchId);
        await TryMarkBatchFailedAsync(batchId, ex.Message, ct);
        await actions.DeadLetterAsync("RetriesExhausted", ex.Message, ct);
    }

    private static bool IsPermanentError(Exception ex) =>
        ex is InvalidTransitionException or PermanentProcessingException;

    private static string GetReason(Exception ex) => ex switch
    {
        InvalidTransitionException => "InvalidTransition",
        PermanentProcessingException => "InvalidMessage",
        _ => "PermanentError"
    };

    private async Task TryMarkBatchFailedAsync(string? batchId, string reason, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(batchId)) return;
        try
        {
            var saga = await _repository.GetAsync(batchId, ct);
            if (saga is null || saga.State is BatchState.Completed or BatchState.Failed or BatchState.Cancelled) return;
            saga.MarkFailed(reason, DateTimeOffset.UtcNow);
            await _repository.UpdateAsync(saga, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to persist Failed state for batch {BatchId}. Message will still be dead-lettered.", batchId);
        }
    }
}
