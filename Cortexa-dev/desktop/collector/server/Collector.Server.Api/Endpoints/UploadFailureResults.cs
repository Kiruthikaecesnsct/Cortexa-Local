using Collector.Server.Application.Errors;

namespace Collector.Server.Api.Endpoints;

public static class UploadFailureResults
{
    private const string PublishStage = "publish";
    private const string RetryHint = "Retry with the same Idempotency-Key.";

    public static IResult? TryMap(Exception exception) => exception switch
    {
        SagaAlreadyExistsException => ErrorResponses.Conflict(
            "upload_in_progress",
            $"An upload with this key is in progress. {RetryHint}"),
        PipelineWriteException { Stage: PublishStage } => ErrorResponses.BadGateway(
            "publish_failed",
            $"Rows are saved but the pipeline was not notified. {RetryHint}"),
        PipelineWriteException => ErrorResponses.Unavailable(
            "storage_unavailable",
            $"Storage failed; nothing was kept. {RetryHint}"),
        _ => null
    };

    public static IResult KeyReused() =>
        ErrorResponses.Conflict(
            "idempotency_key_reused",
            "This Idempotency-Key was used for a different upload. Use a new key.");
}
