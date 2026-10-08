using Collector.Application.Knowledge;
using Collector.Application.Ports;
using Collector.Application.Upload;
using Microsoft.Extensions.Logging;

namespace Collector.Presentation.Services;

public sealed record SelectionRelease(IReadOnlySet<string> UploadedDocuments, IReadOnlyList<string> FailedBatchIds);

public sealed class ReviewUploader(
    UploadKnowledgeHandler handler,
    IBatchStore batchStore,
    IAppVersion appVersion,
    ILogger<ReviewUploader> logger)
{
    public async Task<UploadSession?> PrepareAsync(
        ExtractionRunResult run,
        IReadOnlyList<ExtractedKnowledgeItem> items,
        CancellationToken cancellationToken)
    {
        var request = new UploadRequest
        {
            Items = items,
            Provider = run.Provider,
            Model = run.Model,
            PromptVersion = run.PromptVersion,
            AppVersion = appVersion.Current,
        };
        try
        {
            return await handler.PrepareAsync(request, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception, "Preparing the upload failed.");
            return null;
        }
    }

    public async Task SendAsync(
        UploadSession session,
        IProgress<BatchUploadResult> progress,
        CancellationToken cancellationToken)
    {
        try
        {
            await session.UploadPendingAsync(progress, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception, "Uploading knowledge batches failed unexpectedly.");
        }
    }

    public async Task<SelectionRelease?> ReleaseFailedAsync(UploadSession session, CancellationToken cancellationToken)
    {
        try
        {
            var results = session.Results;
            var failedIds = session.LocalBatchIds.Where((_, index) => results[index].Status == UploadBatchStatus.Failed).ToList();
            var uploadedIds = session.LocalBatchIds.Where((_, index) => results[index].Status == UploadBatchStatus.Uploaded);
            return new SelectionRelease(await DocumentIdsAsync(uploadedIds, cancellationToken), failedIds);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception, "Reading the uploaded batches failed unexpectedly.");
            return null;
        }
    }

    public async Task ReplaceAsync(IReadOnlyCollection<string> batchIds, CancellationToken cancellationToken)
    {
        try
        {
            await batchStore.MarkReplacedAsync(batchIds, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception, "Replacing the failed batches failed unexpectedly.");
        }
    }

    private async Task<HashSet<string>> DocumentIdsAsync(IEnumerable<string> batchIds, CancellationToken cancellationToken)
    {
        var documents = new HashSet<string>(StringComparer.Ordinal);
        foreach (var id in batchIds)
        {
            var stored = await batchStore.GetByIdAsync(id, cancellationToken);
            documents.UnionWith(stored?.DocumentIds ?? []);
        }

        return documents;
    }
}
