using System.Diagnostics;
using Collector.Application.Knowledge;
using Collector.Tests.Support;

namespace Collector.Tests.Knowledge;

public sealed class IdentifierEchoDetectorTests
{
    private const int LargeUnitLength = 200_000;
    private static readonly TimeSpan SpeedBudget = TimeSpan.FromSeconds(3);

    private readonly IdentifierEchoDetector _detector = new();

    public static TheoryData<int, string> EchoCases() => CaseNames("echo-items.json");

    public static TheoryData<int, string> GoodCases() => CaseNames("good-items.json");

    private static TheoryData<int, string> CaseNames(string fixture)
    {
        using var document = KnowledgeFixtures.Load(fixture);
        var data = new TheoryData<int, string>();
        var index = 0;
        foreach (var item in document.RootElement.GetProperty("cases").EnumerateArray())
        {
            data.Add(index++, item.GetProperty("name").GetString()!);
        }

        return data;
    }

    private EchoVerdict Evaluate(string fixture, int index)
    {
        using var document = KnowledgeFixtures.Load(fixture);
        var entry = document.RootElement.GetProperty("cases")[index];
        var item = entry.GetProperty("item");
        return _detector.Evaluate(
            item.GetProperty("title").GetString()!,
            item.GetProperty("summary").GetString()!,
            entry.GetProperty("unit_text").GetString()!);
    }

    [Fact]
    public void Constants_Always_EqualFixtureSpecification()
    {
        using var document = KnowledgeFixtures.Load("echo-items.json");
        var constants = document.RootElement.GetProperty("detector_spec").GetProperty("constants");

        Assert.Equal(IdentifierEchoDetector.TitleEchoRatio, constants.GetProperty("TitleEchoRatio").GetDouble());
        Assert.Equal(IdentifierEchoDetector.SummaryEchoRatio, constants.GetProperty("SummaryEchoRatio").GetDouble());
        Assert.Equal(IdentifierEchoDetector.SummaryCopyRatio, constants.GetProperty("SummaryCopyRatio").GetDouble());
        Assert.Equal(IdentifierEchoDetector.MinCopyCheckLength, constants.GetProperty("MinCopyCheckLength").GetInt32());
    }

    [Theory]
    [MemberData(nameof(EchoCases))]
    public void Evaluate_EchoFixtureCase_FlaggedWithExpectedReason(int index, string name)
    {
        using var document = KnowledgeFixtures.Load("echo-items.json");
        var expected = document.RootElement.GetProperty("cases")[index].GetProperty("expected").GetProperty("reason").GetString();

        var verdict = Evaluate("echo-items.json", index);

        Assert.True(verdict.IsEcho, name);
        Assert.Equal(expected, verdict.Reason);
    }

    [Theory]
    [MemberData(nameof(GoodCases))]
    public void Evaluate_GoodFixtureCase_IsNotEcho(int index, string name)
    {
        var verdict = Evaluate("good-items.json", index);

        Assert.False(verdict.IsEcho, name);
        Assert.Null(verdict.Reason);
    }

    [Fact]
    public void Evaluate_CopiedSummaryAgainstHugeUnit_CompletesQuicklyAndIsFlagged()
    {
        const string Sentence = "the quick brown fox jumps over the lazy dog every single morning";
        var filler = string.Concat(Enumerable.Repeat("lorem ipsum ", LargeUnitLength / 12));
        var unitText = $"{filler}{Sentence}";
        var stopwatch = Stopwatch.StartNew();

        var verdict = _detector.Evaluate("Morning routine of a fox", Sentence, unitText);

        stopwatch.Stop();
        Assert.Equal(EchoReasons.SummaryCopied, verdict.Reason);
        Assert.True(stopwatch.Elapsed < SpeedBudget, $"Took {stopwatch.Elapsed}.");
    }

    [Fact]
    public void Evaluate_CopyRatioExactlyAtThreshold_IsNotEcho()
    {
        var summary = new string('a', 30) + new string('b', 20);
        var unitText = new string('a', 30);

        var verdict = _detector.Evaluate("Plain concept title", summary, unitText);

        Assert.False(verdict.IsEcho);
    }

    [Fact]
    public void Evaluate_CopyRatioAboveThreshold_IsFlaggedAsCopied()
    {
        var summary = new string('a', 31) + new string('b', 19);
        var unitText = new string('a', 31);

        var verdict = _detector.Evaluate("Plain concept title", summary, unitText);

        Assert.Equal(EchoReasons.SummaryCopied, verdict.Reason);
    }

    [Fact]
    public void Evaluate_SummaryJustBelowMinimumLength_SkipsCopyCheck()
    {
        var summary = new string('a', IdentifierEchoDetector.MinCopyCheckLength - 1);

        var verdict = _detector.Evaluate("Plain concept title", summary, summary);

        Assert.False(verdict.IsEcho);
    }

    [Fact]
    public void Evaluate_SummaryAtMinimumLength_RunsCopyCheck()
    {
        var summary = new string('a', IdentifierEchoDetector.MinCopyCheckLength);

        var verdict = _detector.Evaluate("Plain concept title", summary, summary);

        Assert.Equal(EchoReasons.SummaryCopied, verdict.Reason);
    }
}
