using Collector.Application.Knowledge;
using Collector.Application.Upload;
using Microsoft.Extensions.Logging;

namespace Collector.Presentation.Services;

public sealed class ReviewUploader(UploadKnowledgeHandler handler, IAppVersion appVersion, ILogger<ReviewUploader> logger)
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
}
