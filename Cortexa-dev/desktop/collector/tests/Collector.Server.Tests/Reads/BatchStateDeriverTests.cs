using Collector.Server.Application.Reads;
using Collector.Server.Application.Rows;
using Collector.Server.Tests.Fakes;

namespace Collector.Server.Tests.Reads;

public class BatchStateDeriverTests
{
    private const string Batch = "batch-1";

    [Theory]
    [InlineData(RowConstants.SagaDocumentStateComplete, RowConstants.SagaDocumentStateComplete)]
    [InlineData(RowConstants.SagaDocumentStateNoCandidates, RowConstants.SagaDocumentStateNoCandidates)]
    [InlineData(RowConstants.SagaDocumentStateHarvested, RowConstants.SagaDocumentStateSeeded)]
    public void Derive_AllDocumentsTerminalSuccess_ReturnsCompleted(string first, string second)
    {
        var saga = Saga(first, second);

        var state = BatchStateDeriver.Derive(saga);

        Assert.Equal(RowConstants.SagaStateCompleted, state);
    }

    [Fact]
    public void Derive_MixedTerminalFailedAndSuccess_ReturnsFailed()
    {
        var saga = Saga(
            RowConstants.SagaDocumentStateComplete,
            RowConstants.SagaDocumentStateFailed,
            RowConstants.SagaDocumentStateCancelled);

        var state = BatchStateDeriver.Derive(saga);

        Assert.Equal(RowConstants.SagaStateFailed, state);
    }

    [Fact]
    public void Derive_AllDocumentsTerminalFailed_ReturnsFailed()
    {
        var saga = Saga(RowConstants.SagaDocumentStateFailed, RowConstants.SagaDocumentStateCancelled);

        var state = BatchStateDeriver.Derive(saga);

        Assert.Equal(RowConstants.SagaStateFailed, state);
    }

    [Fact]
    public void Derive_FailedDocumentWithStillAdvancingDocument_ReturnsInProgress()
    {
        var saga = Saga(RowConstants.SagaDocumentStateFailed, RowConstants.SagaDocumentStateExtracted);

        var state = BatchStateDeriver.Derive(saga);

        Assert.Equal(RowConstants.SagaStateInProgress, state);
    }

    [Theory]
    [InlineData(RowConstants.SagaDocumentStateQueued)]
    [InlineData(RowConstants.SagaDocumentStateIngested)]
    [InlineData(RowConstants.SagaDocumentStateExtracted)]
    [InlineData(RowConstants.SagaDocumentStateScored)]
    public void Derive_DocumentStillAdvancing_ReturnsInProgress(string state)
    {
        var saga = Saga(state);

        var result = BatchStateDeriver.Derive(saga);

        Assert.Equal(RowConstants.SagaStateInProgress, result);
    }

    [Fact]
    public void Derive_NoDocuments_ReturnsInProgress()
    {
        var saga = Saga();

        var state = BatchStateDeriver.Derive(saga);

        Assert.Equal(RowConstants.SagaStateInProgress, state);
    }

    [Fact]
    public void Derive_ComparesStatesCaseInsensitively()
    {
        var saga = Saga("complete", "nocandidates");

        var state = BatchStateDeriver.Derive(saga);

        Assert.Equal(RowConstants.SagaStateCompleted, state);
    }

    private static SagaRow Saga(params string[] states) =>
        TestSagas.WithDocumentStates(Batch, true, true, states);
}
