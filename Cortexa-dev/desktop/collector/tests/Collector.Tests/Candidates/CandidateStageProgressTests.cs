using Collector.Application.Candidates;

namespace Collector.Tests.Candidates;

public sealed class CandidateStageProgressTests
{
    private static CandidateStageWeights Weights(
        double uploadRead = 0.1,
        double extraction = 0.2,
        double llmCall = 0.6,
        double save = 0.1) => new()
        {
            UploadRead = uploadRead,
            Extraction = extraction,
            LlmCall = llmCall,
            Save = save,
        };

    [Fact]
    public void OverallFraction_NoDocuments_IsZero()
    {
        var progress = new CandidateStageProgress(Weights(), 0);

        Assert.Equal(0d, progress.OverallFraction);
    }

    [Fact]
    public void Report_OutOfRangeIndex_DoesNotThrowAndLeavesOverallUnchanged()
    {
        var progress = new CandidateStageProgress(Weights(), 2);

        var overall = progress.Report(5, CandidateStage.LlmCall, 0.5);

        Assert.Equal(0d, overall);
        Assert.Equal(0d, progress.OverallFraction);
    }

    [Fact]
    public void OverallFraction_IsSumOfPerDocumentFractionDividedByDocumentCount()
    {
        var progress = new CandidateStageProgress(Weights(), 2);

        progress.Report(0, CandidateStage.Save, 1d);
        progress.Report(1, CandidateStage.LlmCall, 0.5);

        Assert.Equal(0.8d, progress.OverallFraction, 6);
    }

    [Fact]
    public void OverallFraction_NeverExceedsOneDespiteFloatingPointRounding()
    {
        var weights = Weights(0.1, 0.1, 0.1, 0.7);
        var progress = new CandidateStageProgress(weights, 1);

        var overall = progress.Report(0, CandidateStage.Save, 1d);

        Assert.True(overall <= 1d);
        Assert.Equal(1d, overall, 6);
    }

    [Fact]
    public void Report_MultipleDocumentsAllComplete_OverallNeverExceedsTotal()
    {
        var progress = new CandidateStageProgress(Weights(), 3);

        progress.Report(0, CandidateStage.Save, 1d);
        progress.Report(1, CandidateStage.Save, 1d);
        var overall = progress.Report(2, CandidateStage.Save, 1d);

        Assert.True(overall <= 1d);
        Assert.Equal(1d, overall, 6);
    }

    [Fact]
    public void Complete_RunFinishesAllStages_OverallFractionReachesOne()
    {
        var progress = new CandidateStageProgress(Weights(), 1);

        progress.Report(0, CandidateStage.UploadRead, 0d);
        progress.Report(0, CandidateStage.Extraction, 0d);
        progress.Report(0, CandidateStage.LlmCall, 0d);
        progress.Report(0, CandidateStage.Save, 0d);
        var overall = progress.Report(0, CandidateStage.Save, 1d);

        Assert.Equal(1d, overall, 6);
    }

    [Fact]
    public void Cancel_DocumentStopsMidStage_OverallStaysAtLastReportedValueAndNeverReachesOne()
    {
        var progress = new CandidateStageProgress(Weights(), 2);
        progress.Report(0, CandidateStage.Save, 1d);

        var overall = progress.Report(1, CandidateStage.LlmCall, 0d);

        Assert.Equal(0.65d, overall, 6);
        Assert.True(overall < 1d);

        var afterNoFurtherReports = progress.OverallFraction;
        Assert.Equal(overall, afterNoFurtherReports, 6);
    }

    [Fact]
    public void Fail_DocumentStopsMidExtraction_OverallHoldsAtLastReportedValue()
    {
        var progress = new CandidateStageProgress(Weights(), 2);
        progress.Report(0, CandidateStage.Save, 1d);

        var overall = progress.Report(1, CandidateStage.Extraction, 0.5d);

        Assert.Equal(0.6d, overall, 6);
        Assert.True(overall < 1d);
    }

    [Fact]
    public void Retry_SameStageReportedAgainAtLowerFraction_DoesNotResetProgress()
    {
        var progress = new CandidateStageProgress(Weights(), 1);
        progress.Report(0, CandidateStage.LlmCall, 0.5d);
        var beforeRetry = progress.FractionOf(0);

        progress.Report(0, CandidateStage.LlmCall, 0d);
        var afterRetry = progress.FractionOf(0);

        Assert.Equal(beforeRetry, afterRetry, 6);
        Assert.Equal(0.6d, afterRetry, 6);
    }

    [Fact]
    public void Retry_ContinuesFromCurrentStageAndStillReachesCompletionAfterwards()
    {
        var progress = new CandidateStageProgress(Weights(), 1);
        progress.Report(0, CandidateStage.LlmCall, 0d);
        progress.Report(0, CandidateStage.LlmCall, 0d);

        progress.Report(0, CandidateStage.Save, 0d);
        var overall = progress.Report(0, CandidateStage.Save, 1d);

        Assert.Equal(1d, overall, 6);
    }

    [Fact]
    public void FractionOf_OutOfRangeIndex_ReturnsZero()
    {
        var progress = new CandidateStageProgress(Weights(), 1);

        Assert.Equal(0d, progress.FractionOf(-1));
        Assert.Equal(0d, progress.FractionOf(9));
    }
}
