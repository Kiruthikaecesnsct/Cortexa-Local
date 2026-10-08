using Collector.Server.Application.Upload.Validation;

namespace Collector.Server.Api.Endpoints;

public sealed record ErrorBody(string Error, string Message, IReadOnlyList<UploadValidationError> Errors);

public static class ErrorResponses
{
    public static IResult BadRequest(string error, string message) =>
        Build(StatusCodes.Status400BadRequest, error, message, []);

    public static IResult Forbidden(string error, string message) =>
        Build(StatusCodes.Status403Forbidden, error, message, []);

    public static IResult Conflict(string error, string message) =>
        Build(StatusCodes.Status409Conflict, error, message, []);

    public static IResult NotFound(string error, string message) =>
        Build(StatusCodes.Status404NotFound, error, message, []);

    public static IResult PayloadTooLarge(string message) =>
        Build(StatusCodes.Status413PayloadTooLarge, "payload_too_large", message, []);

    public static IResult Unprocessable(IReadOnlyList<UploadValidationError> errors) =>
        Build(StatusCodes.Status422UnprocessableEntity, "validation_failed", "The upload failed validation.", errors);

    public static IResult BadGateway(string error, string message) =>
        Build(StatusCodes.Status502BadGateway, error, message, []);

    public static IResult Unavailable(string error, string message) =>
        Build(StatusCodes.Status503ServiceUnavailable, error, message, []);

    private static IResult Build(int statusCode, string error, string message, IReadOnlyList<UploadValidationError> errors) =>
        Results.Json(new ErrorBody(error, message, errors), statusCode: statusCode);
}
