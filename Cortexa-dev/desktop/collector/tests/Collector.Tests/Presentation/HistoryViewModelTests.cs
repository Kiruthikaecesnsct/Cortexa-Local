using Collector.Application.Candidates;
using Collector.Application.History;
using Collector.Domain.Enums;
using Collector.Presentation.Resources;
using Collector.Presentation.ViewModels;
using Collector.Tests.Support;

namespace Collector.Tests.Presentation;

public sealed class HistoryViewModelTests
{
    private const string UserId = "alpha@example.com";

    private readonly InMemoryProcessedDocumentRepository _repository = new();
    private readonly FakeCandidateRunner _runner = new();
    private readonly FakeSession _session = new() { UserEmail = "Alpha@Example.com" };

    [Fact]
    public async Task LoadAsync_SignedInUser_ShowsOnlyTheirDocuments()
    {
        await _repository.UpsertAsync(Record("mine", ProcessedStatus.Succeeded, UserId), TestSupport.Ct);
        await _repository.UpsertAsync(Record("theirs", ProcessedStatus.Succeeded, "other@example.com"), TestSupport.Ct);
        var viewModel = Create();

        await viewModel.LoadAsync(TestSupport.Ct);

        var row = Assert.Single(viewModel.Documents);
        Assert.Equal("mine.txt", row.Name);
        Assert.False(viewModel.IsLoading);
        Assert.False(viewModel.ShowEmpty);
        Assert.True(viewModel.ShowList);
    }

    [Fact]
    public async Task LoadAsync_NoRecords_ShowsEmptyState()
    {
        var viewModel = Create();

        await viewModel.LoadAsync(TestSupport.Ct);

        Assert.True(viewModel.ShowEmpty);
        Assert.Null(viewModel.Banner);
    }

    [Fact]
    public async Task LoadAsync_StoreUnreachable_ShowsErrorBannerAndStopsLoading()
    {
        _repository.Unreachable = true;
        var viewModel = Create();

        await viewModel.LoadAsync(TestSupport.Ct);

        Assert.NotNull(viewModel.Banner);
        Assert.True(viewModel.HasLoadError);
        Assert.False(viewModel.IsLoading);
        Assert.False(viewModel.ShowEmpty);
    }

    [Fact]
    public async Task LoadAsync_SignedOut_ShowsSignInMessage()
    {
        _session.UserEmail = null;
        var viewModel = Create();

        await viewModel.LoadAsync(TestSupport.Ct);

        Assert.True(viewModel.HasLoadError);
        Assert.False(viewModel.IsLoading);
    }

    [Fact]
    public async Task LoadAsync_AfterFailure_RecoversWhenStoreComesBack()
    {
        _repository.Unreachable = true;
        var viewModel = Create();
        await viewModel.LoadAsync(TestSupport.Ct);
        _repository.Unreachable = false;
        await _repository.UpsertAsync(Record("a", ProcessedStatus.Succeeded, UserId), TestSupport.Ct);

        await viewModel.LoadAsync(TestSupport.Ct);

        Assert.False(viewModel.HasLoadError);
        Assert.Null(viewModel.Banner);
        Assert.Single(viewModel.Documents);
    }

    [Fact]
    public async Task Retry_FailedDocument_RerunsItAndReloads()
    {
        var path = Path.Combine(Path.GetTempPath(), $"history-{Guid.NewGuid():N}.txt");
        await File.WriteAllTextAsync(path, "x", TestSupport.Ct);
        try
        {
            await _repository.UpsertAsync(Record("a", ProcessedStatus.Failed, UserId, path), TestSupport.Ct);
            var viewModel = Create();
            await viewModel.LoadAsync(TestSupport.Ct);

            await viewModel.Documents.Single().RetryCommand.ExecuteAsync(null);

            Assert.Equal(["id-a"], _runner.LastDocumentIds);
            Assert.Equal(HistoryStrings.RetryDone("a.txt"), viewModel.StageAnnouncement);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task Retry_LocalSourceMissing_ExplainsAndDoesNotRun()
    {
        await _repository.UpsertAsync(Record("a", ProcessedStatus.Failed, UserId, @"C:\does\not\exist\a.txt"), TestSupport.Ct);
        var viewModel = Create();
        await viewModel.LoadAsync(TestSupport.Ct);

        await viewModel.Documents.Single().RetryCommand.ExecuteAsync(null);

        Assert.Equal(0, _runner.Calls);
        Assert.Equal(HistoryStrings.SourceMissing("a.txt"), viewModel.StageAnnouncement);
    }

    [Fact]
    public async Task RetryCommand_SucceededDocument_IsNotAvailable()
    {
        await _repository.UpsertAsync(Record("a", ProcessedStatus.Succeeded, UserId), TestSupport.Ct);
        var viewModel = Create();
        await viewModel.LoadAsync(TestSupport.Ct);

        Assert.False(viewModel.Documents.Single().RetryCommand.CanExecute(null));
    }

    private HistoryViewModel Create() =>
        new(new HistoryServices(_repository, _runner, _session), new FakeNavigationService());

    private static ProcessedDocumentRecord Record(string name, ProcessedStatus status, string userId, string? path = null) => new()
    {
        UserId = userId,
        DocumentId = $"id-{name}",
        Filename = $"{name}.txt",
        SourceType = SourceType.Local,
        SourceKind = SourceKind.Paper,
        SourcePath = path ?? $"{name}.txt",
        SizeBytes = 1,
        ContentHash = "hash",
        Provider = "Gemini",
        Model = "gemini-test",
        Status = status,
        Outcome = status == ProcessedStatus.Succeeded ? CandidateOutcome.Completed : CandidateOutcome.Failed,
        FailureReason = status == ProcessedStatus.Failed ? "output malformed" : null,
        CreatedAt = DateTimeOffset.UnixEpoch,
        UpdatedAt = DateTimeOffset.UnixEpoch.AddMinutes(1),
    };
}
