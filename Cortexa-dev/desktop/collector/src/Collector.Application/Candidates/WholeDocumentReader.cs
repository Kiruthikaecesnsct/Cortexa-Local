using Collector.Application.Extraction;
using Collector.Application.Ports;
using Collector.Domain.Documents;

namespace Collector.Application.Candidates;

public sealed class WholeDocumentReader(
    IPdfTextExtractor pdfTextExtractor,
    IDocxTextExtractor docxTextExtractor,
    FileContentGuard fileContentGuard,
    TextNormalizer textNormalizer)
{
    private const string BinaryReason = "binary content";
    private const string NoTextReason = "no extractable text";
    private const string TooLargeReason = "document exceeds the size cap";

    public async Task<WholeDocumentRead> ReadAsync(CollectorDocument document, CancellationToken cancellationToken)
    {
        try
        {
            return await ReadCoreAsync(document, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return WholeDocumentRead.Rejected(CandidateOutcome.Failed, exception.Message);
        }
    }

    private async Task<WholeDocumentRead> ReadCoreAsync(CollectorDocument document, CancellationToken cancellationToken)
    {
        return FileClassifier.Classify(document.SourcePath) switch
        {
            FileClassification.Pdf => Finish(document, false, await ReadPdfAsync(document.SourcePath, cancellationToken)),
            FileClassification.Docx => Finish(document, false, await ReadDocxAsync(document.SourcePath, cancellationToken)),
            FileClassification.Code => await ReadGuardedAsync(document, true, cancellationToken),
            _ => await ReadGuardedAsync(document, false, cancellationToken),
        };
    }

    private async Task<string> ReadPdfAsync(string path, CancellationToken cancellationToken)
    {
        var parsed = await pdfTextExtractor.ExtractAsync(path, cancellationToken);
        return textNormalizer.Normalize(parsed.Text);
    }

    private async Task<string> ReadDocxAsync(string path, CancellationToken cancellationToken)
    {
        var parsed = await docxTextExtractor.ExtractAsync(path, cancellationToken);
        return textNormalizer.Normalize(parsed.Text);
    }

    private async Task<WholeDocumentRead> ReadGuardedAsync(
        CollectorDocument document,
        bool isCode,
        CancellationToken cancellationToken)
    {
        var guard = await fileContentGuard.EvaluateAsync(document.SourcePath, cancellationToken);
        if (guard.Outcome == FileGuardOutcome.TooLarge)
        {
            return WholeDocumentRead.Rejected(
                CandidateOutcome.TooLarge,
                TooLargeReason,
                document.SizeBytes,
                FileContentGuard.MaxFileBytes);
        }

        if (!guard.IsAccepted)
        {
            return WholeDocumentRead.Rejected(CandidateOutcome.Failed, BinaryReason);
        }

        return Finish(document, isCode, isCode ? guard.Text! : textNormalizer.Normalize(guard.Text!));
    }

    private static WholeDocumentRead Finish(CollectorDocument document, bool isCode, string text) =>
        string.IsNullOrWhiteSpace(text)
            ? WholeDocumentRead.Rejected(CandidateOutcome.Failed, NoTextReason)
            : WholeDocumentRead.Read(new WholeDocument(document.Id, document.SourcePath, document.Filename, isCode, text));
}
