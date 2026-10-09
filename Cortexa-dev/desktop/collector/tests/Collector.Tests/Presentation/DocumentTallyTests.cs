using Collector.Domain.Enums;
using Collector.Presentation.ViewModels;

namespace Collector.Tests.Presentation;

public sealed class DocumentTallyTests
{
    private const int FirstUnits = 4;
    private const int FirstTokens = 400;
    private const int FirstPrompt = 900;
    private const int SecondUnits = 2;
    private const int SecondTokens = 150;
    private const int SecondPrompt = 300;

    private static DocumentRowViewModel Row(
        string name,
        DocumentStatus status = DocumentStatus.Extracted,
        int units = 0,
        int tokens = 0,
        int prompt = 0,
        string? id = null) =>
        new(SplitOutcomes.Make(name, status, units: units, tokens: tokens, promptTokens: prompt, documentId: id));

    [Fact]
    public void Add_AnalyzedRow_AccumulatesCountsAndTotals()
    {
        var tally = new DocumentTally();

        tally.Add(Row("a.md", units: FirstUnits, tokens: FirstTokens, prompt: FirstPrompt, id: "doc-1"));
        tally.Add(Row("b.md", units: SecondUnits, tokens: SecondTokens, prompt: SecondPrompt, id: "doc-2"));

        Assert.Equal(2, tally.Files);
        Assert.Equal(2, tally.Analyzed);
        Assert.Equal(FirstUnits + SecondUnits, tally.Units);
        Assert.Equal(FirstTokens + SecondTokens, tally.Tokens);
        Assert.Equal(FirstPrompt + SecondPrompt, tally.PromptTokens);
        Assert.Equal(["doc-1", "doc-2"], tally.AnalyzedIds);
    }

    [Fact]
    public void Add_FailedAndExcludedRows_CountAsSkippedAndAreNotTrackedAsAnalyzed()
    {
        var tally = new DocumentTally();

        tally.Add(Row("a.md", DocumentStatus.Failed, id: "doc-1"));
        tally.Add(Row("b.md", DocumentStatus.Excluded, id: "doc-2"));

        Assert.Equal(1, tally.Failed);
        Assert.Equal(1, tally.Excluded);
        Assert.Equal(2, tally.Skipped);
        Assert.Equal(0, tally.Analyzed);
        Assert.Empty(tally.AnalyzedIds);
    }

    [Fact]
    public void Add_AnalyzedRowWithoutDocumentId_IsCountedButNotTracked()
    {
        var tally = new DocumentTally();

        tally.Add(Row("a.md"));

        Assert.Equal(1, tally.Analyzed);
        Assert.Empty(tally.AnalyzedIds);
    }

    [Fact]
    public void Remove_PreviouslyAddedRow_RestoresEveryTotal()
    {
        var tally = new DocumentTally();
        var keep = Row("a.md", units: FirstUnits, tokens: FirstTokens, prompt: FirstPrompt, id: "doc-1");
        var drop = Row("b.md", units: SecondUnits, tokens: SecondTokens, prompt: SecondPrompt, id: "doc-2");
        tally.Add(keep);
        tally.Add(drop);

        tally.Remove(drop);

        Assert.Equal(1, tally.Files);
        Assert.Equal(FirstUnits, tally.Units);
        Assert.Equal(FirstTokens, tally.Tokens);
        Assert.Equal(FirstPrompt, tally.PromptTokens);
        Assert.Equal(["doc-1"], tally.AnalyzedIds);
    }

    [Fact]
    public void Remove_SkippedRow_DecrementsSkipped()
    {
        var tally = new DocumentTally();
        var failed = Row("a.md", DocumentStatus.Failed);
        tally.Add(failed);

        tally.Remove(failed);

        Assert.Equal(0, tally.Skipped);
        Assert.Equal(0, tally.Files);
    }

    [Fact]
    public void Reset_AfterAdds_ZeroesEverything()
    {
        var tally = new DocumentTally();
        tally.Add(Row("a.md", units: FirstUnits, tokens: FirstTokens, prompt: FirstPrompt, id: "doc-1"));
        tally.Add(Row("b.md", DocumentStatus.Failed));

        tally.Reset();

        Assert.Equal(0, tally.Files);
        Assert.Equal(0, tally.Analyzed);
        Assert.Equal(0, tally.Skipped);
        Assert.Equal(0, tally.Units);
        Assert.Equal(0, tally.Tokens);
        Assert.Equal(0, tally.PromptTokens);
        Assert.Empty(tally.AnalyzedIds);
    }
}
