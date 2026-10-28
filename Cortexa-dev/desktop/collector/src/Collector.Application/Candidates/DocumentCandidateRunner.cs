using Collector.Application.Ports;
using Collector.Domain.Documents;
using Collector.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace Collector.Application.Candidates;

public sealed record DocumentRun(CandidateDocumentResult Result, string? Model);

public sealed class DocumentCandidateRunner(
    DocumentPreparer preparer,
    IAiProviderFactory providerFactory,
    ReplyInterpreter interpreter,
    ILogger<DocumentCandidateRunner> logger)
{
    public async Task<DocumentRun> RunAsync(CollectorDocument document, CancellationToken cancellationToken)
    {
        try
        {
            var run = await RunCoreAsync(document, cancellationToken);
            LogOutcome(document, run.Result);
            return run;
        }
        catch (AiInputTooLargeException exception)
        {
            return TooLargeRun(document, exception);
        }
        catch (AiProviderException exception)
        {
            logger.LogWarning(
                "Candidate run for document {DocumentId} failed with provider error {Kind} (status {StatusCode}).",
                document.Id,
                exception.Kind,
                exception.StatusCode);
            return ReplyInterpreter.Fail(document, CandidateOutcomeMapper.FromException(exception));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new DocumentRun(CandidateDocumentResult.Canceled(document.Id, document.Filename), null);
        }
        catch (Exception exception)
        {
            logger.LogWarning("Candidate run for document {DocumentId} failed with {ErrorType}.", document.Id, exception.GetType().Name);
            return ReplyInterpreter.Fail(document, new CandidateFailure(CandidateOutcome.Failed, "unexpected error"));
        }
    }

    private void LogOutcome(CollectorDocument document, CandidateDocumentResult result)
    {
        if (result.Outcome != CandidateOutcome.Completed)
        {
            logger.LogWarning("Candidate run for document {DocumentId} ended as {Outcome}: {Reason}.", document.Id, result.Outcome, result.Reason);
        }
    }

    private DocumentRun TooLargeRun(CollectorDocument document, AiInputTooLargeException exception)
    {
        logger.LogWarning(
            "Document {DocumentId} has {TokenCount} tokens, over the {Limit} limit of model {Model}.",
            document.Id,
            exception.TokenCount,
            exception.Limit,
            exception.Model);
        return new DocumentRun(DocumentPreparer.TooLarge(document, exception.TokenCount, exception.Limit), null);
    }

    private async Task<DocumentRun> RunCoreAsync(CollectorDocument document, CancellationToken cancellationToken)
    {
        var preparation = await preparer.PrepareAsync(document, cancellationToken);
        if (preparation.Prepared is not { } prepared)
        {
            return new DocumentRun(preparation.Rejection!, null);
        }

        var completion = await providerFactory.Resolve(CollectorProvider.Gemini).CompleteAsync(prepared.Request, cancellationToken);
        return interpreter.Interpret(document, prepared, completion);
    }
}
