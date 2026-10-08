using Collector.Domain.History;
using Collector.Presentation.Resources;
using Collector.Presentation.ViewModels;

namespace Collector.Tests.Presentation;

public sealed class CandidateRowTests
{
    private readonly HistoryHarness _harness = new();

    private CandidateRowViewModel Row(BatchCandidate candidate) =>
        new(candidate, new HistorySourceContext("batch-1", _harness.Resolver, _harness.Launcher));

    [Theory]
    [InlineData(78.5, "79")]
    [InlineData(78.4999, "78")]
    [InlineData(0.5, "1")]
    [InlineData(-12.0, "0")]
    [InlineData(140.2, "100")]
    [InlineData(100.0, "100")]
    public void Score_ClampsAndRoundsAwayFromZero(double value, string expected)
    {
        Assert.Equal(expected, CandidateFormat.Score(value));
    }

    [Fact]
    public void Score_NullOrNaN_IsNull()
    {
        Assert.Null(CandidateFormat.Score(null));
        Assert.Null(CandidateFormat.Score(double.NaN));
    }

    [Theory]
    [InlineData(64, "64")]
    [InlineData(-4, "0")]
    [InlineData(250, "100")]
    public void Patentability_ClampsToRange(int value, string expected)
    {
        Assert.Equal(expected, CandidateFormat.Patentability(value));
    }

    [Theory]
    [InlineData("data_model", "Data model")]
    [InlineData("key-content", "Key content")]
    [InlineData("  novel__method ", "Novel method")]
    [InlineData("API", "Api")]
    [InlineData("", "")]
    public void Kind_IsHumanized(string raw, string expected)
    {
        Assert.Equal(expected, CandidateFormat.Kind(raw));
    }

    [Fact]
    public void Row_MissingScores_ShowDashWithTooltipAndWordsInAutomationName()
    {
        var row = Row(HistoryData.Candidate("c1", score: null, patentability: null));

        Assert.Equal(HistoryStrings.NullMetric, row.Metrics[0].Text);
        Assert.True(row.Metrics[0].IsNull);
        Assert.Equal(HistoryStrings.NotScored, row.Metrics[0].ToolTip);
        Assert.Equal(HistoryStrings.NotRated, row.Metrics[1].ToolTip);
        Assert.False(row.Metrics[2].IsNull);
        Assert.Contains("score not scored yet", row.AutomationName);
        Assert.Contains("patentability not rated yet", row.AutomationName);
        Assert.DoesNotContain(HistoryStrings.NullMetric, row.AutomationName);
    }

    [Fact]
    public void Row_Scored_FormatsMetricsAndAutomationName()
    {
        var row = Row(HistoryData.Candidate("c1", links: [HistoryData.Link()]));

        Assert.Equal(["79", "64", "3"], row.Metrics.Select(metric => metric.Text));
        Assert.Equal("Title c1, Harvesting, Data model, score 79, patentability 64, 3 evidence, 1 linked item", row.AutomationName);
        Assert.Equal(HistoryStrings.LinkedCount(1), row.LinkedText);
    }

    [Fact]
    public void Update_ChangesFieldsInPlace_KeepsExpansionAndLinkInstances()
    {
        var row = Row(HistoryData.Candidate("c1", links: [HistoryData.Link()]));
        row.IsExpanded = true;
        var link = row.Links[0];

        row.Update(HistoryData.Candidate("c1", score: 91, links: [HistoryData.Link(), HistoryData.Link(itemId: "item-2")]));

        Assert.True(row.IsExpanded);
        Assert.Same(link, row.Links[0]);
        Assert.Equal(2, row.Links.Count);
        Assert.Equal("91", row.Metrics[0].Text);
    }

    [Fact]
    public void Update_LinkRemoved_DropsItsRow()
    {
        var row = Row(HistoryData.Candidate("c1", links: [HistoryData.Link(), HistoryData.Link(itemId: "item-2")]));

        row.Update(HistoryData.Candidate("c1", links: [HistoryData.Link(itemId: "item-2")]));

        Assert.Single(row.Links);
        Assert.True(row.HasLinks);
        row.Update(HistoryData.Candidate("c1"));
        Assert.True(row.HasNoLinks);
    }

    [Fact]
    public async Task Link_UnresolvedSource_ShowsNotFoundAndCannotOpen()
    {
        var row = Row(HistoryData.Candidate("c1", links: [HistoryData.Link()]));
        var link = row.Links[0];

        await link.Resolution;

        Assert.True(link.ShowNotFound);
        Assert.False(link.OpenSourceCommand.CanExecute(null));
        Assert.Equal(HistoryStrings.SourceNotFound, link.NotFoundHint);
        Assert.Equal("Page 4", link.SourceText);
    }

    [Fact]
    public async Task Link_ResolvedSource_OpensLocalPathOnly()
    {
        var documentId = await _harness.AddLocalDocumentAsync("batch-1", "C:/work/paper.pdf");
        var link = new CandidateKnowledgeLink
        {
            KnowledgeItem = HistoryData.Link().KnowledgeItem,
            Source = new CandidateSource { DocumentId = documentId, FilePath = "/server/secret/other.cs", LineStart = 4, LineEnd = 9 },
        };
        var row = Row(HistoryData.Candidate("c1", links: [link]));
        var rowLink = row.Links[0];

        await rowLink.Resolution;
        rowLink.OpenSourceCommand.Execute(null);

        Assert.True(rowLink.OpenSourceCommand.CanExecute(null));
        Assert.Equal("/server/secret/other.cs · lines 4–9", rowLink.SourceText);
        var launched = Assert.Single(_harness.Launcher.Launched);
        Assert.Equal("C:/work/paper.pdf", launched.Path);
        Assert.Equal(Collector.Domain.Enums.SourceKind.Paper, launched.SourceKind);
    }

    [Fact]
    public async Task Link_LaunchFails_BecomesUnavailable()
    {
        var documentId = await _harness.AddLocalDocumentAsync("batch-1", "C:/work/paper.pdf");
        _harness.Launcher.Result = false;
        var row = Row(HistoryData.Candidate("c1", links: [HistoryData.Link(documentId)]));
        var link = row.Links[0];
        await link.Resolution;

        link.OpenSourceCommand.Execute(null);

        Assert.False(link.IsAvailable);
        Assert.True(link.ShowNotFound);
        Assert.False(link.OpenSourceCommand.CanExecute(null));
    }
}
