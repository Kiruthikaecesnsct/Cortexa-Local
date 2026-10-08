using Collector.Domain.History;
using Collector.Server.Application.Reads;
using Collector.Server.Application.Rows;
using Collector.Server.Tests.Fakes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Collector.Server.Tests.Reads;

public class BatchStageCalculatorTests
{
    private const string Batch = "batch-1";

    private static readonly BatchStageCalculator Calculator = new(NullLogger<BatchStageCalculator>.Instance);

    [Theory]
    [InlineData(RowConstants.SagaDocumentStateQueued, BatchStage.Ingested)]
    [InlineData(RowConstants.SagaDocumentStateIngested, BatchStage.Ingested)]
    [InlineData(RowConstants.SagaDocumentStateExtracted, BatchStage.Extracted)]
    [InlineData(RowConstants.SagaDocumentStateScored, BatchStage.Scored)]
    [InlineData(RowConstants.SagaDocumentStateHarvested, BatchStage.Harvested)]
    [InlineData(RowConstants.SagaDocumentStateSeeded, BatchStage.Seeded)]
    public void Calculate_SingleDocument_MapsStateToStage(string state, BatchStage expected)
    {
        var summary = Calculator.Calculate(Saga(true, true, state));

        Assert.Equal(expected, summary.Stage);
    }

    [Theory]
    [InlineData(RowConstants.SagaDocumentStateComplete)]
    [InlineData(RowConstants.SagaDocumentStateNoCandidates)]
    public void Calculate_TerminalState_IsSeededWhenSeedingWanted(string state)
    {
        var summary = Calculator.Calculate(Saga(true, true, state));

        Assert.Equal(BatchStage.Seeded, summary.Stage);
        Assert.Equal(1, summary.SeedingCompleted);
    }

    [Theory]
    [InlineData(RowConstants.SagaDocumentStateComplete)]
    [InlineData(RowConstants.SagaDocumentStateNoCandidates)]
    public void Calculate_TerminalState_IsHarvestedWhenSeedingNotWanted(string state)
    {
        var summary = Calculator.Calculate(Saga(true, false, state));

        Assert.Equal(BatchStage.Harvested, summary.Stage);
        Assert.Equal(0, summary.SeedingCompleted);
        Assert.Equal(0, summary.SeedingTotal);
    }

    [Fact]
    public void Calculate_UsesLowestRankAmongDocuments()
    {
        var summary = Calculator.Calculate(Saga(
            true,
            true,
            RowConstants.SagaDocumentStateSeeded,
            RowConstants.SagaDocumentStateExtracted,
            RowConstants.SagaDocumentStateHarvested));

        Assert.Equal(BatchStage.Extracted, summary.Stage);
    }

    [Fact]
    public void Calculate_SkipsFailedAndCancelledDocuments()
    {
        var summary = Calculator.Calculate(Saga(
            true,
            true,
            RowConstants.SagaDocumentStateFailed,
            RowConstants.SagaDocumentStateCancelled,
            RowConstants.SagaDocumentStateScored));

        Assert.Equal(BatchStage.Scored, summary.Stage);
    }

    [Fact]
    public void Calculate_AllDocumentsFailed_FallsBackToIngested()
    {
        var summary = Calculator.Calculate(Saga(
            true,
            true,
            RowConstants.SagaDocumentStateFailed,
            RowConstants.SagaDocumentStateCancelled));

        Assert.Equal(BatchStage.Ingested, summary.Stage);
    }

    [Fact]
    public void Calculate_NoDocuments_IsIngestedWithZeroCounts()
    {
        var summary = Calculator.Calculate(Saga(true, true));

        Assert.Equal(new BatchStageSummary(BatchStage.Ingested, 0, 0, 0, 0), summary);
    }

    [Fact]
    public void Calculate_ComparesStatesCaseInsensitively()
    {
        var summary = Calculator.Calculate(Saga(true, true, "harvested"));

        Assert.Equal(BatchStage.Harvested, summary.Stage);
    }

    [Fact]
    public void Calculate_CountsHarvestingAtHarvestedOrLater()
    {
        var summary = Calculator.Calculate(Saga(
            true,
            true,
            RowConstants.SagaDocumentStateScored,
            RowConstants.SagaDocumentStateHarvested,
            RowConstants.SagaDocumentStateSeeded,
            RowConstants.SagaDocumentStateComplete));

        Assert.Equal(3, summary.HarvestingCompleted);
        Assert.Equal(4, summary.HarvestingTotal);
        Assert.Equal(2, summary.SeedingCompleted);
        Assert.Equal(4, summary.SeedingTotal);
    }

    [Fact]
    public void Calculate_HarvestingNotWanted_ReportsZeroHarvestingCounts()
    {
        var summary = Calculator.Calculate(Saga(false, true, RowConstants.SagaDocumentStateSeeded));

        Assert.Equal(0, summary.HarvestingCompleted);
        Assert.Equal(0, summary.HarvestingTotal);
    }

    [Fact]
    public void Calculate_UnknownState_IsIngestedAndLoggedOnce()
    {
        var logger = new CountingLogger();
        var calculator = new BatchStageCalculator(logger);

        var first = calculator.Calculate(Saga(true, true, "Mystery"));
        calculator.Calculate(Saga(true, true, "mystery"));

        Assert.Equal(BatchStage.Ingested, first.Stage);
        Assert.Equal(1, logger.WarningCount);
    }

    private static SagaRow Saga(bool wantsHarvesting, bool wantsSeeding, params string[] states) =>
        TestSagas.WithDocumentStates(Batch, wantsHarvesting, wantsSeeding, states);

    private sealed class CountingLogger : ILogger<BatchStageCalculator>
    {
        public int WarningCount { get; private set; }

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.Warning)
            {
                WarningCount++;
            }
        }
    }
}
