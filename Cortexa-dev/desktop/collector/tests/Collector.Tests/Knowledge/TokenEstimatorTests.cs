using Collector.Application.Knowledge;
using Collector.Domain.Extraction;
using Collector.Tests.Extraction;
using Collector.Tests.Support;

namespace Collector.Tests.Knowledge;

public sealed class TokenEstimatorTests
{
    private readonly WordCountTokenCounter _tokenCounter = new();
    private readonly TokenEstimator _estimator;

    public TokenEstimatorTests()
    {
        _estimator = new TokenEstimator(KnowledgePipeline.Builder(), _tokenCounter);
    }

    private int PromptTokensFor(ExtractionUnit unit)
    {
        var request = KnowledgePipeline.Builder().Build(unit);
        return _tokenCounter.Count(request.SystemText) + _tokenCounter.Count(request.UserText) + _tokenCounter.Count(request.OutputSchemaJson);
    }

    [Fact]
    public void Estimate_NoUnits_ReturnsZeroForBothTotals()
    {
        var estimate = _estimator.Estimate([], maxOutputTokens: 8192);

        Assert.Equal(0, estimate.PromptTokens);
        Assert.Equal(0, estimate.EstimatedOutputTokens);
        Assert.Equal(0, estimate.Total);
    }

    [Fact]
    public void Estimate_SingleUnit_PromptTokensMatchTheBuiltPromptWrapper()
    {
        var unit = TestData.FileUnit("a reasonably sized block of source text to extract from");

        var estimate = _estimator.Estimate([unit], maxOutputTokens: 8192);

        Assert.Equal(PromptTokensFor(unit), estimate.PromptTokens);
    }

    [Fact]
    public void Estimate_MultipleUnits_PromptTokensSumAcrossUnits()
    {
        var first = TestData.FileUnit("first unit body text here", id: "unit-1");
        var second = TestData.FileUnit("second unit body text here as well", id: "unit-2");

        var estimate = _estimator.Estimate([first, second], maxOutputTokens: 8192);

        Assert.Equal(PromptTokensFor(first) + PromptTokensFor(second), estimate.PromptTokens);
    }

    [Fact]
    public void Estimate_OutputBelowCap_IsAFractionOfPromptTokens()
    {
        var unit = TestData.FileUnit("short text");

        var estimate = _estimator.Estimate([unit], maxOutputTokens: 100_000);

        Assert.Equal((int)Math.Ceiling(estimate.PromptTokens * 0.35), estimate.EstimatedOutputTokens);
        Assert.True(estimate.EstimatedOutputTokens < estimate.PromptTokens);
    }

    [Fact]
    public void Estimate_OutputAboveCap_IsClampedToMaxOutputTokens()
    {
        var unit = TestData.FileUnit(string.Join(' ', Enumerable.Repeat("word", 500)));

        var estimate = _estimator.Estimate([unit], maxOutputTokens: 10);

        Assert.Equal(10, estimate.EstimatedOutputTokens);
    }

    [Theory]
    [InlineData(0, 8192)]
    [InlineData(1, 8192)]
    [InlineData(1000, 8192)]
    [InlineData(1000, 10)]
    public void FromPromptTokens_AnyCountAndCap_MatchesEstimateOverTheSameUnits(int wordCount, int maxOutputTokens)
    {
        ExtractionUnit[] units = wordCount == 0
            ? []
            : [TestData.FileUnit(string.Join(' ', Enumerable.Repeat("word", wordCount)))];
        var promptTokens = _estimator.CountPromptTokens(units);

        var fromTotals = _estimator.FromPromptTokens(promptTokens, maxOutputTokens);

        Assert.Equal(_estimator.Estimate(units, maxOutputTokens), fromTotals);
    }

    [Fact]
    public void FromPromptTokens_SumOfPerUnitCounts_EqualsEstimateOverAllUnits()
    {
        var first = TestData.FileUnit("first unit body text here", id: "unit-1");
        var second = TestData.FileUnit("second unit body text here as well", id: "unit-2");
        var summed = _estimator.CountPromptTokens([first]) + _estimator.CountPromptTokens([second]);

        var incremental = _estimator.FromPromptTokens(summed, maxOutputTokens: 8192);

        Assert.Equal(_estimator.Estimate([first, second], maxOutputTokens: 8192), incremental);
    }

    [Fact]
    public void Estimate_Total_IsPromptPlusEstimatedOutput()
    {
        var unit = TestData.FileUnit("text for the total check");

        var estimate = _estimator.Estimate([unit], maxOutputTokens: 8192);

        Assert.Equal(estimate.PromptTokens + estimate.EstimatedOutputTokens, estimate.Total);
    }
}
