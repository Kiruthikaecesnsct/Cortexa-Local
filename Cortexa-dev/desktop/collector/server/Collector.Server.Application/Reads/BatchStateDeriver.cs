using Collector.Server.Application.Rows;

namespace Collector.Server.Application.Reads;

public static class BatchStateDeriver
{
    private static readonly HashSet<string> TerminalSuccessStates = new(StringComparer.OrdinalIgnoreCase)
    {
        RowConstants.SagaDocumentStateComplete,
        RowConstants.SagaDocumentStateNoCandidates,
        RowConstants.SagaDocumentStateHarvested,
        RowConstants.SagaDocumentStateSeeded
    };

    private static readonly HashSet<string> TerminalFailedStates = new(StringComparer.OrdinalIgnoreCase)
    {
        RowConstants.SagaDocumentStateFailed,
        RowConstants.SagaDocumentStateCancelled
    };

    public static string Derive(SagaRow saga)
    {
        if (saga.Documents.Count == 0)
        {
            return RowConstants.SagaStateInProgress;
        }

        if (saga.Documents.All(document => TerminalSuccessStates.Contains(document.State)))
        {
            return RowConstants.SagaStateCompleted;
        }

        var hasFailedDocument = saga.Documents.Any(document => TerminalFailedStates.Contains(document.State));
        var hasAdvancingDocument = saga.Documents.Any(IsAdvancing);

        return hasFailedDocument && !hasAdvancingDocument
            ? RowConstants.SagaStateFailed
            : RowConstants.SagaStateInProgress;
    }

    private static bool IsAdvancing(SagaDocumentProgressRow document) =>
        !TerminalSuccessStates.Contains(document.State) && !TerminalFailedStates.Contains(document.State);
}
