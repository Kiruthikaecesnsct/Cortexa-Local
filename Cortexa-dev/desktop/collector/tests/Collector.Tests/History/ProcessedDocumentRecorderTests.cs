using Collector.Application.Candidates;
using Collector.Application.History;
using Collector.Domain.Documents;
using Collector.Domain.Enums;
using Collector.Tests.Extraction;
using Collector.Tests.Presentation;
using Collector.Tests.Support;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Collector.Tests.History;

public sealed class ProcessedDocumentRecorderTests
{
    private const string DocumentName = "a.txt";
    private const string Email = "Alpha@Example.COM";

    private readonly InMemoryDocumentStore _documents = new();
    private readonly InMemoryProcessedDocumentRepository _repository = new();
    private readonly FakeSession _session = new() { UserEmail = Email };

    [Fact]
    public async Task RecordAsync_CompletedDocument_SavesSucceededRecordForLowercasedUser()
    {
        Seed();

        await Recorder().RecordAsync(CandidateData.Result(CandidateData.Completed(DocumentName)), TestSupport.Ct);

        var record = Assert.Single(_repository.All);
        Assert.Equal("alpha@example.com", record.UserId);
        Assert.Equal(ProcessedStatus.Succeeded, record.Status);
        Assert.Null(record.FailureReason);
        Assert.False(record.CanRetry);
        Assert.Single(record.Candidates);
        Assert.Equal(CandidateData.Model, record.Model);
    }

    [Fact]
    public async Task RecordAsync_FailedDocument_KeepsReasonAndAllowsRetry()
    {
        Seed();
        var failed = CandidateDocumentResult.Failure($"id-{DocumentName}", DocumentName, CandidateOutcome.Failed, "output malformed");

        await Recorder().RecordAsync(CandidateData.Result(failed), TestSupport.Ct);

        var record = Assert.Single(_repository.All);
        Assert.Equal(ProcessedStatus.Failed, record.Status);
        Assert.Equal("output malformed", record.FailureReason);
        Assert.True(record.CanRetry);
    }

    [Fact]
    public async Task RecordAsync_RerunAfterFailure_ReplacesTheSameRecord()
    {
        Seed();
        var failed = CandidateDocumentResult.Failure($"id-{DocumentName}", DocumentName, CandidateOutcome.Failed, "output malformed");
        await Recorder().RecordAsync(CandidateData.Result(failed), TestSupport.Ct);

        await Recorder().RecordAsync(CandidateData.Result(CandidateData.Completed(DocumentName)), TestSupport.Ct);

        var record = Assert.Single(_repository.All);
        Assert.Equal(ProcessedStatus.Succeeded, record.Status);
    }

    [Fact]
    public async Task RecordAsync_RerunSucceeds_KeepsTheOriginalCreatedAt()
    {
        Seed();
        var first = new FakeTimeProvider(DateTimeOffset.Parse("2026-10-01T00:00:00Z"));
        await Recorder(first).RecordAsync(CandidateData.Result(CandidateData.Completed(DocumentName)), TestSupport.Ct);
        var later = new FakeTimeProvider(DateTimeOffset.Parse("2026-10-20T00:00:00Z"));

        await Recorder(later).RecordAsync(CandidateData.Result(CandidateData.Completed(DocumentName)), TestSupport.Ct);

        var record = Assert.Single(_repository.All);
        Assert.Equal(DateTimeOffset.Parse("2026-10-01T00:00:00Z"), record.CreatedAt);
        Assert.Equal(DateTimeOffset.Parse("2026-10-20T00:00:00Z"), record.UpdatedAt);
    }

    [Fact]
    public async Task RecordAsync_FailedRerunOfASucceededDocument_KeepsTheGoodRecord()
    {
        Seed();
        await Recorder().RecordAsync(CandidateData.Result(CandidateData.Completed(DocumentName)), TestSupport.Ct);
        var failed = CandidateDocumentResult.Failure($"id-{DocumentName}", DocumentName, CandidateOutcome.Quota, "rate limited");

        await Recorder().RecordAsync(CandidateData.Result(failed), TestSupport.Ct);

        var record = Assert.Single(_repository.All);
        Assert.Equal(ProcessedStatus.Succeeded, record.Status);
        Assert.Single(record.Candidates);
    }

    [Fact]
    public async Task RecordAsync_CanceledDocument_IsNotSaved()
    {
        Seed();
        var canceled = CandidateDocumentResult.Canceled($"id-{DocumentName}", DocumentName);

        await Recorder().RecordAsync(CandidateData.Result(canceled), TestSupport.Ct);

        Assert.Empty(_repository.All);
    }

    [Fact]
    public async Task RecordAsync_SignedOut_SavesNothing()
    {
        Seed();
        _session.UserEmail = null;

        await Recorder().RecordAsync(CandidateData.Result(CandidateData.Completed(DocumentName)), TestSupport.Ct);

        Assert.Empty(_repository.All);
    }

    [Fact]
    public async Task RecordAsync_StoreUnreachable_DoesNotThrow()
    {
        Seed();
        _repository.Unreachable = true;

        await Recorder().RecordAsync(CandidateData.Result(CandidateData.Completed(DocumentName)), TestSupport.Ct);

        Assert.Empty(_repository.All);
    }

    private ProcessedDocumentRecorder Recorder(TimeProvider? time = null) =>
        new(_documents, _repository, _session, time ?? TimeProvider.System, NullLogger<ProcessedDocumentRecorder>.Instance);

    private void Seed() =>
        _documents.ByPath[DocumentName] = new CollectorDocument
        {
            Id = $"id-{DocumentName}",
            SourceType = SourceType.Local,
            SourceKind = SourceKind.Paper,
            SourcePath = DocumentName,
            Filename = DocumentName,
            ContentHash = "hash",
            SizeBytes = 1,
            Status = DocumentStatus.Pending,
            CreatedAt = DateTimeOffset.UnixEpoch,
            UpdatedAt = DateTimeOffset.UnixEpoch,
        };
}
