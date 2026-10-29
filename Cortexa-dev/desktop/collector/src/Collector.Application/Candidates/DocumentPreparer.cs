using Collector.Application.Ports;
using Collector.Domain.Documents;

namespace Collector.Application.Candidates;

public sealed record PreparedDocument(WholeDocument Whole, AiRequest Request, InputLimitResult Limit);

public sealed record PreparationResult
{
    public PreparedDocument? Prepared { get; private init; }

    public CandidateDocumentResult? Rejection { get; private init; }

    public static PreparationResult Ready(PreparedDocument prepared) => new() { Prepared = prepared };

    public static PreparationResult Rejected(CandidateDocumentResult rejection) => new() { Rejection = rejection };
}

public sealed class DocumentPreparer(
    WholeDocumentReader reader,
    CandidatePromptBuilder promptBuilder,
    GeminiInputLimitCheck limitCheck)
{
    private const string TooLargeReason = "document exceeds the model input limit";

    public async Task<PreparationResult> PrepareAsync(
        CollectorDocument document,
        Action<CandidateStage, double> reportStage,
        CancellationToken cancellationToken)
    {
        reportStage(CandidateStage.UploadRead, 0d);
        var read = await reader.ReadAsync(document, cancellationToken);
        if (!read.IsRead)
        {
            return PreparationResult.Rejected(RejectedRead(document, read));
        }

        reportStage(CandidateStage.Extraction, 0d);
        var request = promptBuilder.Build(read.Document!);
        var limit = await limitCheck.CheckAsync(request, cancellationToken);
        return limit.IsWithin
            ? PreparationResult.Ready(new PreparedDocument(read.Document!, request, limit))
            : PreparationResult.Rejected(TooLarge(document, limit.TokenCount, limit.Limit));
    }

    private static CandidateDocumentResult RejectedRead(CollectorDocument document, WholeDocumentRead read) =>
        CandidateDocumentResult.Failure(document.Id, document.Filename, read.Outcome, read.Reason)
            with
        { SizeBytes = read.SizeBytes, SizeCapBytes = read.CapBytes };

    internal static CandidateDocumentResult TooLarge(CollectorDocument document, int tokenCount, int limit) =>
        CandidateDocumentResult.Failure(document.Id, document.Filename, CandidateOutcome.TooLarge, TooLargeReason)
            with
        { TokenCount = tokenCount, TokenLimit = limit };
}
