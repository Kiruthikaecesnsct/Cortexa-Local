using Collector.Application.Auth;
using Collector.Application.Candidates;
using Collector.Application.Ports;
using Collector.Domain.Documents;
using Collector.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace Collector.Application.History;

public sealed class ProcessedDocumentRecorder(
    IDocumentStore documentStore,
    IProcessedDocumentRepository repository,
    ISessionState session,
    TimeProvider time,
    ILogger<ProcessedDocumentRecorder> logger)
{
    public async Task RecordAsync(CandidateRunResult run, CancellationToken cancellationToken)
    {
        var userId = UserKey.From(session.UserEmail);
        if (userId is null)
        {
            return;
        }

        foreach (var result in run.Documents.Where(result => result.Outcome != CandidateOutcome.Canceled))
        {
            await RecordOneAsync(userId, run.Model, result, cancellationToken);
        }
    }

    private async Task RecordOneAsync(string userId, string model, CandidateDocumentResult result, CancellationToken cancellationToken)
    {
        try
        {
            var document = await documentStore.GetAsync(result.DocumentId, cancellationToken);
            if (document is not null)
            {
                await SaveAsync(ToRecord(userId, model, document, result), cancellationToken);
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning("Saving history for document {DocumentId} failed ({ErrorType}, cause {CauseType}).", result.DocumentId, exception.GetType().Name, (exception.InnerException ?? exception).GetType().Name);
        }
    }

    private async Task SaveAsync(ProcessedDocumentRecord next, CancellationToken cancellationToken)
    {
        var existing = await repository.GetAsync(next.UserId, next.DocumentId, cancellationToken);
        if (existing is { Status: ProcessedStatus.Succeeded } && next.Status == ProcessedStatus.Failed)
        {
            logger.LogInformation("Keeping the saved result for document {DocumentId}; the re-run failed.", next.DocumentId);
            return;
        }

        await repository.UpsertAsync(existing is null ? next : next with { CreatedAt = existing.CreatedAt }, cancellationToken);
    }

    private ProcessedDocumentRecord ToRecord(string userId, string model, CollectorDocument document, CandidateDocumentResult result)
    {
        var now = time.GetUtcNow();
        return new ProcessedDocumentRecord
        {
            UserId = userId,
            DocumentId = document.Id,
            Filename = document.Filename,
            SourceType = document.SourceType,
            SourceKind = document.SourceKind,
            SourcePath = document.SourcePath,
            SizeBytes = document.SizeBytes,
            ContentHash = document.ContentHash,
            Provider = CollectorProvider.Gemini.ToString(),
            Model = model,
            Status = result.Outcome == CandidateOutcome.Completed ? ProcessedStatus.Succeeded : ProcessedStatus.Failed,
            Outcome = result.Outcome,
            FailureReason = result.Outcome == CandidateOutcome.Completed ? null : result.Reason ?? result.Outcome.ToString(),
            Candidates = result.Candidates,
            CreatedAt = now,
            UpdatedAt = now,
        };
    }
}

public static class UserKey
{
    public static string? From(string? email) =>
        string.IsNullOrWhiteSpace(email) ? null : email.Trim().ToLowerInvariant();
}
