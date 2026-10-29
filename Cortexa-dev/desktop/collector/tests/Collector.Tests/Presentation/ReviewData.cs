using Collector.Application.Candidates;
using Collector.Application.Knowledge;
using Collector.Application.Ports;
using Collector.Domain.Enums;
using Collector.Presentation.Services;
using Collector.Presentation.ViewModels;
using Collector.Tests.Extraction;
using Collector.Tests.Support;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace Collector.Tests.Presentation;

internal static class ReviewData
{
    public const string Model = "claude-test";
    public const string PromptVersion = "knowledge.v1";
    private const int DefaultTotalUnits = 5;

    public static ExtractedKnowledgeItem Item(
        string title,
        string documentId = TestData.DocumentId,
        KnowledgeKind kind = KnowledgeKind.Method,
        bool echo = false) =>
        TestData.Item(title, documentId: documentId, kind: kind) with
        {
            EchoVerdict = echo ? new EchoVerdict(true, EchoReasons.TitleIsIdentifier) : EchoVerdict.Clean,
        };

    public static List<ExtractedKnowledgeItem> Items(string documentId, int count, KnowledgeKind kind = KnowledgeKind.Method) =>
        [.. Enumerable.Range(0, count).Select(index => Item($"{documentId} idea {index}", documentId, kind))];

    public static ExtractionRunResult Run(IReadOnlyList<ExtractedKnowledgeItem> items, int failedUnits = 0, int skippedUnits = 0) => new()
    {
        Items = items,
        TotalUnits = DefaultTotalUnits,
        FailedUnits = failedUnits,
        SkippedUnits = skippedUnits,
        FailedDocumentIds = [],
        Provider = CollectorProvider.Claude,
        Model = Model,
        PromptVersion = PromptVersion,
    };
}

internal sealed class ReviewHarness
{
    public ReviewHarness()
    {
        ViewModel = new ReviewViewModel(
            State,
            Navigation,
            PdfExporter,
            FilePicker,
            Session,
            new FakeTimeProvider(DateTimeOffset.Parse("2026-10-07T09:30:00Z")),
            new CandidatesPanelViewModel(CandidateState));
    }

    public CandidateRunState CandidateState { get; } = new(Options.Create(new CandidateStageWeights()));

    public KnowledgeRunState State { get; } = new();

    public InMemoryDocumentStore Documents { get; } = new();

    public FakeNavigationService Navigation { get; } = new();

    public FakeKnowledgePdfExporter PdfExporter { get; } = new();

    public FakeFilePicker FilePicker { get; } = new([]);

    public FakeSession Session { get; } = new();

    public ReviewViewModel ViewModel { get; }

    public ReviewViewModel Open(params ExtractedKnowledgeItem[] items) => Open(ReviewData.Run(items));

    public ReviewViewModel Open(ExtractionRunResult run)
    {
        foreach (var id in run.Items.Select(item => item.DocumentId).Distinct())
        {
            var document = TestData.Document(id);
            Documents.ByPath[document.SourcePath] = document;
        }

        State.Set(run);
        ViewModel.OnNavigatedTo();
        return ViewModel;
    }

}

internal static class ReviewViewModelExtensions
{
    public static IReadOnlyList<KnowledgeItemRowViewModel> ItemRows(this ReviewViewModel viewModel) =>
        [.. viewModel.VisibleRows.OfType<KnowledgeItemRowViewModel>()];

    public static IReadOnlyList<DocumentHeaderRowViewModel> Headers(this ReviewViewModel viewModel) =>
        [.. viewModel.VisibleRows.OfType<DocumentHeaderRowViewModel>()];

    public static KnowledgeItemRowViewModel Row(this ReviewViewModel viewModel, string title) =>
        viewModel.ItemRows().Single(row => row.Title == title);

    public static void SelectFilter(this ReviewViewModel viewModel, KnowledgeKind? kind)
    {
        foreach (var filter in viewModel.Filters.Where(filter => filter.Kind != kind))
        {
            filter.IsSelected = false;
        }

        viewModel.Filters.Single(filter => filter.Kind == kind).IsSelected = true;
    }
}
