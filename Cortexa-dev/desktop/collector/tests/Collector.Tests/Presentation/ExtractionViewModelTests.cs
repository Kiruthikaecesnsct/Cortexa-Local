using Collector.Application.Extraction;
using Collector.Application.Knowledge;
using Collector.Application.Remote;
using Collector.Application.Settings;
using Collector.Domain.Enums;
using Collector.Domain.Remote;
using Collector.Infrastructure.Options;
using Collector.Presentation.Navigation;
using Collector.Presentation.Services;
using Collector.Presentation.ViewModels;
using Collector.Tests.Extraction;
using Collector.Tests.Support;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace Collector.Tests.Presentation;

public sealed class ExtractionViewModelTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"collector-extraction-vm-{Guid.NewGuid():N}");
    private readonly InMemoryUnitStore _unitStore = new();
    private readonly ExtractionService _extractionService;
    private readonly KnowledgeHarness _knowledge = new();
    private readonly RemoteSourceHarness _remote = new();
    private readonly ModelChoiceHarness _model = new();
    private readonly FakePdfTextExtractor _pdf = new();
    private readonly FakeTimeProvider _time = new(DateTimeOffset.Parse("2026-10-07T00:00:00Z"));
    private readonly ExtractionOptions _splitOptions = new();
    private readonly TokenEstimator _estimator = new(KnowledgePipeline.Builder(), new WordCountTokenCounter());
    private readonly ParallelSplitter _splitter;
    private readonly ProviderOutputLimits _limits = new(
        Options.Create(new AiProviderOptions()),
        Options.Create(new GeminiProviderOptions()),
        Options.Create(new BedrockProviderOptions()));

    public ExtractionViewModelTests()
    {
        Directory.CreateDirectory(_directory);
        var tokenCounter = new WordCountTokenCounter();
        _extractionService = new ExtractionService(
            _pdf,
            new FakeDocxTextExtractor(),
            tokenCounter,
            new InMemoryDocumentStore(),
            _unitStore,
            new FileContentGuard(),
            new TextNormalizer(),
            new CodeSplitter(tokenCounter),
            new UnitBuilder(new HeadingDetector()),
            new DocumentStatusRules(),
            NullLogger<ExtractionService>.Instance);
        _splitter = new ParallelSplitter(
            _extractionService,
            _estimator,
            Options.Create(_splitOptions),
            NullLogger<ParallelSplitter>.Instance);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private string WriteFile(string name, byte[] content)
    {
        var path = Path.Combine(_directory, name);
        File.WriteAllBytes(path, content);
        return path;
    }

    private ExtractionViewModel CreateViewModel(params string[] paths)
    {
        var viewModel = Build(paths);
        viewModel.OnNavigatedTo();
        return viewModel;
    }

    private ExtractionViewModel Build(params string[] paths) =>
        new(
            new ExtractionDependencies(
                _extractionService,
                _unitStore,
                new FakeFilePicker(paths),
                _remote.ViewModel,
                NullLogger<ExtractionViewModel>.Instance,
                _remote.Intake,
                _splitter,
                _time),
            _knowledge.ViewModel,
            new TokenEstimationDependencies(
                _estimator,
                _limits),
            _model.CreateActiveModel());

    [Fact]
    public Task Picking_files_extracts_each_one_and_populates_the_documents_list() =>
        UiThreadHost.RunAsync(async () =>
        {
            var path = WriteFile("notes.txt", System.Text.Encoding.UTF8.GetBytes("# Heading\nsome body content here."));
            var viewModel = CreateViewModel(path);

            await viewModel.PickFilesCommand.ExecuteAsync(null);

            Assert.Equal(1, viewModel.FilesCount);
            Assert.Equal(1, viewModel.ExtractedCount);
            Assert.Equal(0, viewModel.SkippedCount);
            Assert.True(viewModel.TotalUnits > 0);
            Assert.NotNull(viewModel.SelectedDocument);
            Assert.Equal(DocumentStatus.Extracted, viewModel.SelectedDocument!.Status);
        });

    [Fact]
    public Task Default_selection_is_the_first_extracted_document() =>
        UiThreadHost.RunAsync(async () =>
        {
            var huge = WriteFile("huge.cs", new byte[FileContentGuard.MaxFileBytes + 1]);
            var ok = WriteFile("notes.txt", System.Text.Encoding.UTF8.GetBytes("some readable content"));
            var viewModel = CreateViewModel(huge, ok);

            await viewModel.PickFilesCommand.ExecuteAsync(null);

            Assert.NotNull(viewModel.SelectedDocument);
            Assert.Equal("notes.txt", viewModel.SelectedDocument!.Filename);
            Assert.NotEmpty(viewModel.PreviewUnits);
        });

    [Fact]
    public Task Partial_skip_raises_a_warning_banner() =>
        UiThreadHost.RunAsync(async () =>
        {
            var huge = WriteFile("huge.cs", new byte[FileContentGuard.MaxFileBytes + 1]);
            var ok = WriteFile("notes.txt", System.Text.Encoding.UTF8.GetBytes("some readable content"));
            var viewModel = CreateViewModel(huge, ok);

            await viewModel.PickFilesCommand.ExecuteAsync(null);

            Assert.True(viewModel.HasSkipped);
            Assert.False(viewModel.AllSkipped);
            Assert.NotNull(viewModel.SkipBanner);
            Assert.Equal(BannerSeverity.Warning, viewModel.SkipBanner!.Severity);
        });

    [Fact]
    public Task All_files_skipped_raises_an_error_banner_and_clears_selection() =>
        UiThreadHost.RunAsync(async () =>
        {
            var huge = WriteFile("huge.cs", new byte[FileContentGuard.MaxFileBytes + 1]);
            var viewModel = CreateViewModel(huge);

            await viewModel.PickFilesCommand.ExecuteAsync(null);

            Assert.True(viewModel.AllSkipped);
            Assert.Null(viewModel.SelectedDocument);
            Assert.Equal(BannerSeverity.Error, viewModel.SkipBanner!.Severity);
            Assert.True(viewModel.ShowPreviewPlaceholder);
        });

    [Fact]
    public Task Selecting_a_non_extracted_document_shows_the_preview_empty_state() =>
        UiThreadHost.RunAsync(async () =>
        {
            var huge = WriteFile("huge.cs", new byte[FileContentGuard.MaxFileBytes + 1]);
            var ok = WriteFile("notes.txt", System.Text.Encoding.UTF8.GetBytes("some readable content"));
            var viewModel = CreateViewModel(huge, ok);
            await viewModel.PickFilesCommand.ExecuteAsync(null);

            viewModel.SelectedDocument = viewModel.Documents.Single(d => d.Filename == "huge.cs");

            Assert.True(viewModel.ShowPreviewEmpty);
            Assert.False(viewModel.ShowPreviewUnits);
        });

    [Fact]
    public Task Clear_all_resets_documents_preview_and_banner() =>
        UiThreadHost.RunAsync(async () =>
        {
            var ok = WriteFile("notes.txt", System.Text.Encoding.UTF8.GetBytes("some readable content"));
            var viewModel = CreateViewModel(ok);
            await viewModel.PickFilesCommand.ExecuteAsync(null);

            viewModel.ClearAllCommand.Execute(null);

            Assert.Empty(viewModel.Documents);
            Assert.Empty(viewModel.PreviewUnits);
            Assert.Null(viewModel.SelectedDocument);
            Assert.Null(viewModel.SkipBanner);
            Assert.Equal(0, viewModel.FilesCount);
        });

    [Fact]
    public Task Removing_the_selected_document_falls_back_to_another_extracted_document() =>
        UiThreadHost.RunAsync(async () =>
        {
            var first = WriteFile("first.txt", System.Text.Encoding.UTF8.GetBytes("first content"));
            var second = WriteFile("second.txt", System.Text.Encoding.UTF8.GetBytes("second content"));
            var viewModel = CreateViewModel(first, second);
            await viewModel.PickFilesCommand.ExecuteAsync(null);
            var selected = viewModel.SelectedDocument!;

            viewModel.RemoveDocumentCommand.Execute(selected);

            Assert.DoesNotContain(selected, viewModel.Documents);
            Assert.NotNull(viewModel.SelectedDocument);
            Assert.NotEqual(selected, viewModel.SelectedDocument);
        });

    [Fact]
    public Task Selecting_another_document_before_a_slow_preview_load_finishes_keeps_only_the_latest_units() =>
        UiThreadHost.RunAsync(async () =>
        {
            var first = WriteFile("first.txt", System.Text.Encoding.UTF8.GetBytes("first content"));
            var second = WriteFile("second.txt", System.Text.Encoding.UTF8.GetBytes("second content"));
            var viewModel = CreateViewModel(first, second);
            await viewModel.PickFilesCommand.ExecuteAsync(null);
            var rowA = viewModel.Documents.Single(d => d.Filename == "first.txt");
            var rowB = viewModel.Documents.Single(d => d.Filename == "second.txt");
            viewModel.SelectedDocument = rowB;
            var gate = new TaskCompletionSource();
            _unitStore.Gates[rowA.DocumentId!] = gate;
            viewModel.SelectedDocument = rowA;

            viewModel.SelectedDocument = rowB;
            gate.SetResult();
            await Task.Yield();

            Assert.NotEmpty(viewModel.PreviewUnits);
            Assert.All(viewModel.PreviewUnits, u => Assert.Contains("second", u.Snippet, StringComparison.Ordinal));
        });

    [Fact]
    public Task ExtractKnowledge_SkippedAndExtractedDocuments_PassesOnlyExtractedIds() =>
        UiThreadHost.RunAsync(async () =>
        {
            var huge = WriteFile("huge.cs", new byte[FileContentGuard.MaxFileBytes + 1]);
            var ok = WriteFile("notes.txt", System.Text.Encoding.UTF8.GetBytes("some readable content"));
            var viewModel = CreateViewModel(huge, ok);
            await viewModel.PickFilesCommand.ExecuteAsync(null);
            var extractedId = viewModel.Documents.Single(d => d.Status == DocumentStatus.Extracted).DocumentId!;

            await viewModel.Knowledge.ExtractKnowledgeCommand.ExecuteAsync(null);

            Assert.Equal([extractedId], _knowledge.Runner.LastDocumentIds);
        });

    [Fact]
    public Task DocumentCommands_KnowledgeRunInFlight_CannotExecute() =>
        UiThreadHost.RunAsync(async () =>
        {
            var ok = WriteFile("notes.txt", System.Text.Encoding.UTF8.GetBytes("some readable content"));
            var viewModel = CreateViewModel(ok);
            await viewModel.PickFilesCommand.ExecuteAsync(null);
            var row = viewModel.Documents.Single();
            _knowledge.Runner.Gate = new TaskCompletionSource<KnowledgeRunOutcome>();

            var run = viewModel.Knowledge.ExtractKnowledgeCommand.ExecuteAsync(null);

            Assert.False(viewModel.PickFilesCommand.CanExecute(null));
            Assert.False(viewModel.ClearAllCommand.CanExecute(null));
            Assert.False(viewModel.RemoveDocumentCommand.CanExecute(row));
            _knowledge.Runner.Gate.SetResult(new KnowledgeRunOutcome(KnowledgeRunStatus.Failed));
            await run;
            Assert.True(viewModel.PickFilesCommand.CanExecute(null));
            Assert.True(viewModel.ClearAllCommand.CanExecute(null));
        });

    [Fact]
    public Task ExtractKnowledge_RunStarted_ForwardsCancelFocusRequest() =>
        UiThreadHost.RunAsync(async () =>
        {
            var ok = WriteFile("notes.txt", System.Text.Encoding.UTF8.GetBytes("some readable content"));
            var viewModel = CreateViewModel(ok);
            await viewModel.PickFilesCommand.ExecuteAsync(null);
            _knowledge.Runner.Gate = new TaskCompletionSource<KnowledgeRunOutcome>();

            var run = viewModel.Knowledge.ExtractKnowledgeCommand.ExecuteAsync(null);

            Assert.Equal(KnowledgeFocusKeys.Cancel, viewModel.PendingFocus);
            _knowledge.Runner.Gate.SetResult(new KnowledgeRunOutcome(KnowledgeRunStatus.Failed));
            await run;
        });

    [Fact]
    public Task ExtractKnowledge_ProviderKeyMissing_IsBlockedUntilTheKeyIsAdded() =>
        UiThreadHost.RunAsync(async () =>
        {
            var ok = WriteFile("notes.txt", System.Text.Encoding.UTF8.GetBytes("some readable content"));
            _model.Readiness.States[CollectorProvider.Claude] = ProviderReadinessState.Missing;
            var viewModel = CreateViewModel(ok);
            await viewModel.PickFilesCommand.ExecuteAsync(null);

            Assert.False(viewModel.Knowledge.CanExtract);

            _model.Readiness.States[CollectorProvider.Claude] = ProviderReadinessState.Ready;
            _model.Readiness.NotifyChanged();

            Assert.True(viewModel.Knowledge.CanExtract);
        });

    [Fact]
    public Task ActiveModel_ChoiceChanged_PushesProviderAndModelIntoTheRun() =>
        UiThreadHost.RunAsync(async () =>
        {
            var ok = WriteFile("notes.txt", System.Text.Encoding.UTF8.GetBytes("some readable content"));
            var viewModel = CreateViewModel(ok);
            await viewModel.PickFilesCommand.ExecuteAsync(null);

            await _model.Choices.SaveAsync(new AiModelChoice(CollectorProvider.Gemini, "gemini-b"), TestContext.Current.CancellationToken);
            await viewModel.Knowledge.ExtractKnowledgeCommand.ExecuteAsync(null);

            Assert.Equal(CollectorProvider.Gemini, _knowledge.Runner.LastRequest!.Provider);
            Assert.Equal("gemini-b", _knowledge.Runner.LastRequest.Model);
        });

    [Fact]
    public void OnNavigatedTo_RefreshesReadinessSoAKeyAddedInSettingsUnblocksExtract()
    {
        _model.Readiness.States[CollectorProvider.Claude] = ProviderReadinessState.Missing;
        var viewModel = CreateViewModel();
        Assert.Equal(ProviderReadinessState.Missing, viewModel.Knowledge.Readiness);

        _model.Readiness.States[CollectorProvider.Claude] = ProviderReadinessState.Ready;
        viewModel.OnNavigatedTo();

        Assert.Equal(ProviderReadinessState.Ready, viewModel.Knowledge.Readiness);
    }

    [Fact]
    public Task ExtractKnowledge_NoFilesPicked_IsNotAvailable() =>
        UiThreadHost.RunAsync(async () =>
        {
            var viewModel = CreateViewModel();
            await viewModel.PickFilesCommand.ExecuteAsync(null);

            Assert.False(viewModel.Knowledge.CanExtract);
        });

    private ExtractionViewModel BuildGated(out string first)
    {
        _splitOptions.MaxParallelSplits = 1;
        _pdf.Gate = new TaskCompletionSource();
        first = WriteFile("first.txt", System.Text.Encoding.UTF8.GetBytes("# First\nfirst document body text."));
        var blocked = WriteFile("blocked.pdf", [1, 2, 3]);
        var last = WriteFile("last.txt", System.Text.Encoding.UTF8.GetBytes("# Last\nlast document body text, a bit longer."));
        return CreateViewModel(first, blocked, last);
    }

    [Fact]
    public Task Documents_AreAddedOnlyWhenTheFlushIntervalElapses() =>
        UiThreadHost.RunAsync(async () =>
        {
            var viewModel = BuildGated(out _);

            var run = viewModel.PickFilesCommand.ExecuteAsync(null);
            await _pdf.Entered.Task;

            Assert.Empty(viewModel.Documents);
            _time.Advance(IntakeTiming.FlushInterval);
            Assert.Single(viewModel.Documents);
            _pdf.Gate!.SetResult();
            await run;
            Assert.Equal(3, viewModel.Documents.Count);
        });

    [Fact]
    public Task Documents_FlushIntervalNotYetElapsed_StayEmpty() =>
        UiThreadHost.RunAsync(async () =>
        {
            var viewModel = BuildGated(out _);

            var run = viewModel.PickFilesCommand.ExecuteAsync(null);
            await _pdf.Entered.Task;
            _time.Advance(IntakeTiming.FlushInterval - TimeSpan.FromMilliseconds(1));

            Assert.Empty(viewModel.Documents);
            _pdf.Gate!.SetResult();
            await run;
        });

    [Fact]
    public Task RunFinished_FlushesPendingDocumentsWithoutWaitingForTheTimer() =>
        UiThreadHost.RunAsync(async () =>
        {
            var path = WriteFile("notes.txt", System.Text.Encoding.UTF8.GetBytes("some readable content"));
            var viewModel = CreateViewModel(path);

            await viewModel.PickFilesCommand.ExecuteAsync(null);

            Assert.Single(viewModel.Documents);
            Assert.False(viewModel.Intake.IsActive);
            Assert.False(viewModel.IsExtracting);
        });

    [Fact]
    public Task AnalyzedDocument_ShowsTheAnalyzedStatusLabel() =>
        UiThreadHost.RunAsync(async () =>
        {
            var path = WriteFile("notes.txt", System.Text.Encoding.UTF8.GetBytes("some readable content"));
            var viewModel = CreateViewModel(path);

            await viewModel.PickFilesCommand.ExecuteAsync(null);

            Assert.Equal("Analyzed", viewModel.Documents.Single().StatusLabel);
            Assert.Equal(1, viewModel.AnalyzedCount);
        });

    [Fact]
    public Task Estimate_AcrossIncrementalFlushes_EqualsTheEstimatorOverAllUnits() =>
        UiThreadHost.RunAsync(async () =>
        {
            var viewModel = BuildGated(out _);

            var run = viewModel.PickFilesCommand.ExecuteAsync(null);
            await _pdf.Entered.Task;
            _time.Advance(IntakeTiming.FlushInterval);
            var afterFirstFlush = viewModel.PromptTokens;
            _pdf.Gate!.SetResult();
            await run;

            var units = _unitStore.ByDocumentId.Values.SelectMany(list => list).ToList();
            var expected = _estimator.Estimate(units, _limits.MaxOutputTokensFor(viewModel.ActiveModel.Provider));
            Assert.True(afterFirstFlush > 0);
            Assert.True(viewModel.PromptTokens > afterFirstFlush);
            Assert.Equal(expected.PromptTokens, viewModel.PromptTokens);
            Assert.Equal(expected.EstimatedOutputTokens, viewModel.EstimatedOutputTokens);
        });

    [Fact]
    public Task CancelActiveWork_WhileSplitting_KeepsDeliveredDocumentsAndEndsTheIntake() =>
        UiThreadHost.RunAsync(async () =>
        {
            var viewModel = BuildGated(out _);

            var run = viewModel.PickFilesCommand.ExecuteAsync(null);
            await _pdf.Entered.Task;
            Assert.True(viewModel.CancelActiveWorkCommand.CanExecute(null));
            viewModel.CancelActiveWorkCommand.Execute(null);
            await run;

            Assert.Single(viewModel.Documents);
            Assert.False(viewModel.Intake.IsActive);
            Assert.False(viewModel.IsExtracting);
        });

    [Fact]
    public Task RemoteFilesFetched_SplitsEachFileUsingItsRepoPathForNameAndFolder() =>
        UiThreadHost.RunAsync(async () =>
        {
            const string RepoPath = "docs/guides/setup.txt";
            var path = WriteFile("cached-blob.txt", System.Text.Encoding.UTF8.GetBytes("some readable content"));
            const string Origin = "octo/alpha @ main";
            var viewModel = CreateViewModel();
            _remote.Fetcher.Result = new RemoteFetchResult(SourceType.Github, "3f2a9c1e55", [new FetchedFile(path, RepoPath)], 1, 0, 0, 0, false);
            _remote.GitHub.Branches = [new RemoteBranch("main", "3f2a9c1e55")];
            await _remote.OpenGitHubAsync(RemoteSourceHarness.GitHubRepo("alpha"));
            _remote.ViewModel.SelectedRepository = _remote.ViewModel.Repositories[0];
            _remote.ViewModel.SelectedBranch = _remote.ViewModel.Branches[0];

            await _remote.ViewModel.FetchCommand.ExecuteAsync(null);
            await WaitForDocumentsAsync(viewModel, 1);

            var row = viewModel.Documents.Single();
            Assert.Equal("setup.txt", row.Filename);
            Assert.Equal("docs/guides", row.Folder);
            Assert.Equal(Origin, row.Origin);
        });

    private static async Task WaitForDocumentsAsync(ExtractionViewModel viewModel, int count)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (viewModel.Documents.Count < count)
        {
            Assert.True(DateTime.UtcNow < deadline, "Timed out waiting for documents.");
            await Task.Delay(5, TestContext.Current.CancellationToken);
        }
    }
}
