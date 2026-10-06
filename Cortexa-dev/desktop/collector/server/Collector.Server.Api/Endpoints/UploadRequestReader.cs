using System.Text.Json;
using Collector.Domain.Serialization;
using Collector.Domain.Upload;
using Collector.Server.Api.Auth;
using Collector.Server.Application.Upload;

namespace Collector.Server.Api.Endpoints;

public static class UploadRequestReader
{
    public static async Task<UploadRequestInput> ReadAsync(
        HttpContext http,
        UploadOptions options,
        CancellationToken cancellationToken)
    {
        if (!CollectorClaims.TryGetCaller(http.User, out var caller))
        {
            return UploadRequestInput.Rejected(
                ErrorResponses.Forbidden("account_invalid", "The token is missing required identity claims."));
        }

        if (!IdempotencyKeys.TryRead(http.Request, options.MaxIdempotencyKeyLength, out var key))
        {
            return UploadRequestInput.Rejected(
                ErrorResponses.BadRequest("invalid_idempotency_key", "A valid Idempotency-Key header is required."));
        }

        var body = await RequestBodyReader.ReadAsync(http.Request, options.MaxRequestBytes, cancellationToken);
        if (body is null)
        {
            return UploadRequestInput.Rejected(ErrorResponses.PayloadTooLarge("The request body is too large."));
        }

        var request = Deserialize(body);
        return request is null
            ? UploadRequestInput.Rejected(ErrorResponses.BadRequest("invalid_body", "The request body is not valid."))
            : UploadRequestInput.Accepted(new SubmitKnowledgeUploadCommand(request, caller, key));
    }

    private static KnowledgeUploadRequest? Deserialize(byte[] body)
    {
        try
        {
            return JsonSerializer.Deserialize<KnowledgeUploadRequest>(body, CollectorJson.Options);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
