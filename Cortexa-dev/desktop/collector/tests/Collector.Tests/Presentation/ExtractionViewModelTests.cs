using Collector.Application.Extraction;
using Collector.Application.Knowledge;
using Collector.Domain.Enums;
using Collector.Infrastructure.Options;
using Collector.Presentation.Services;
using Collector.Presentation.ViewModels;
using Collector.Tests.Extraction;
using Collector.Tests.Support;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Collector.Tests.Presentation;

public sealed class ExtractionViewModelTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"collector-extraction-vm-{Guid.NewGuid():N}");
    private readonly InMemoryUnitStore _unitStore = new();
    private readonly ExtractionService _extractionService;
    private readonly KnowledgeHarness _knowledge = new();

    public ExtractionViewModelTests()
    {
        Directory.CreateDirectory(_directory);
        var tokenCounter = new WordCountTokenCounter();
        _extractionService = new ExtractionService(
            new FakePdfTextExtractor(),
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

    private ExtractionViewModel CreateViewModel(params string[] paths) =>
        new(
            new ExtractionDependencies(_extractionService, _unitStore, new FakeFilePicker(paths), NullLogger<ExtractionViewModel>.Instance),
            _knowledge.ViewModel,
            new TokenEstimationDependencies(
                new TokenEstimator(KnowledgePipeline.Builder(), new WordCountTokenCounter()),
                new ProviderOutputLimits(
                    Options.Create(new AiProviderOptions()),
                    Options.Create(new GeminiProviderOptions()),
                    Options.Create(new BedrockProviderOptions()))),
            Options.Create(new ProviderModelCatalog()));

    [Fact]
    public async Task Picking_files_extracts_each_one_and_populates_the_documents_list()
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
    }

    [Fact]
    public async Task Default_selection_is_the_first_extracted_document()
    {
        var huge = WriteFile("huge.cs", new byte[FileContentGuard.MaxFileBytes + 1]);
        var ok = WriteFile("notes.txt", System.Text.Encoding.UTF8.GetBytes("some readable content"));
        var viewModel = CreateViewModel(huge, ok);

        await viewModel.PickFilesCommand.ExecuteAsync(null);

        Assert.NotNull(viewModel.SelectedDocument);
        Assert.Equal("notes.txt", viewModel.SelectedDocument!.Filename);
        Assert.NotEmpty(viewModel.PreviewUnits);
    }

    [Fact]
    public async Task Partial_skip_raises_a_warning_banner()
    {
        var huge = WriteFile("huge.cs", new byte[FileContentGuard.MaxFileBytes + 1]);
        var ok = WriteFile("notes.txt", System.Text.Encoding.UTF8.GetBytes("some readable content"));
        var viewModel = CreateViewModel(huge, ok);

        await viewModel.PickFilesCommand.ExecuteAsync(null);

        Assert.True(viewModel.HasSkipped);
        Assert.False(viewModel.AllSkipped);
        Assert.NotNull(viewModel.SkipBanner);
        Assert.Equal(BannerSeverity.Warning, viewModel.SkipBanner!.Severity);
    }

    [Fact]
    public async Task All_files_skipped_raises_an_error_banner_and_clears_selection()
    {
        var huge = WriteFile("huge.cs", new byte[FileContentGuard.MaxFileBytes + 1]);
        var viewModel = CreateViewModel(huge);

        await viewModel.PickFilesCommand.ExecuteAsync(null);

        Assert.True(viewModel.AllSkipped);
        Assert.Null(viewModel.SelectedDocument);
        Assert.Equal(BannerSeverity.Error, viewModel.SkipBanner!.Severity);
        Assert.True(viewModel.ShowPreviewPlaceholder);
    }

    [Fact]
    public async Task Selecting_a_non_extracted_document_shows_the_preview_empty_state()
    {
        var huge = WriteFile("huge.cs", new byte[FileContentGuard.MaxFileBytes + 1]);
        var ok = WriteFile("notes.txt", System.Text.Encoding.UTF8.GetBytes("some readable content"));
        var viewModel = CreateViewModel(huge, ok);
        await viewModel.PickFilesCommand.ExecuteAsync(null);

        viewModel.SelectedDocument = viewModel.Documents.Single(d => d.Filename == "huge.cs");

        Assert.True(viewModel.ShowPreviewEmpty);
        Assert.False(viewModel.ShowPreviewUnits);
    }

    [Fact]
    public async Task Clear_all_resets_documents_preview_and_banner()
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
    }

    [Fact]
    public async Task Removing_the_selected_document_falls_back_to_another_extracted_document()
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
    }

    [Fact]
    public async Task Selecting_another_document_before_a_slow_preview_load_finishes_keeps_only_the_latest_units()
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
    }

    [Fact]
    public async Task ExtractKnowledge_SkippedAndExtractedDocuments_PassesOnlyExtractedIds()
    {
        var huge = WriteFile("huge.cs", new byte[FileContentGuard.MaxFileBytes + 1]);
        var ok = WriteFile("notes.txt", System.Text.Encoding.UTF8.GetBytes("some readable content"));
        var viewModel = CreateViewModel(huge, ok);
        await viewModel.PickFilesCommand.ExecuteAsync(null);
        var extractedId = viewModel.Documents.Single(d => d.Status == DocumentStatus.Extracted).DocumentId!;

        await viewModel.Knowledge.ExtractKnowledgeCommand.ExecuteAsync(null);

        Assert.Equal([extractedId], _knowledge.Runner.LastDocumentIds);
    }

    [Fact]
    public async Task DocumentCommands_KnowledgeRunInFlight_CannotExecute()
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
    }

    [Fact]
    public async Task ExtractKnowledge_RunStarted_ForwardsCancelFocusRequest()
    {
        var ok = WriteFile("notes.txt", System.Text.Encoding.UTF8.GetBytes("some readable content"));
        var viewModel = CreateViewModel(ok);
        await viewModel.PickFilesCommand.ExecuteAsync(null);
        _knowledge.Runner.Gate = new TaskCompletionSource<KnowledgeRunOutcome>();

        var run = viewModel.Knowledge.ExtractKnowledgeCommand.ExecuteAsync(null);

        Assert.Equal(KnowledgeFocusKeys.Cancel, viewModel.PendingFocus);
        _knowledge.Runner.Gate.SetResult(new KnowledgeRunOutcome(KnowledgeRunStatus.Failed));
        await run;
    }

    [Fact]
    public async Task ExtractKnowledge_NoFilesPicked_IsNotAvailable()
    {
        var viewModel = CreateViewModel();
        await viewModel.PickFilesCommand.ExecuteAsync(null);

        Assert.False(viewModel.Knowledge.CanExtract);
    }
}
