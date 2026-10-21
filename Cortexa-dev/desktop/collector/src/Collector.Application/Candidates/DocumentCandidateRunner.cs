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
            return await RunCoreAsync(document, cancellationToken);
        }
        catch (AiProviderException exception)
        {
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
