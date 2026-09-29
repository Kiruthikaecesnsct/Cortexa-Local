namespace Cortexa.Identity.Api;

internal static class ApiEnvelope
{
    internal static IResult Success<T>(T data) =>
        Results.Ok(new { success = true, data });

    internal static IResult Error(int status, string errorCode, string message) =>
        Results.Json(
            new { success = false, error_code = errorCode, message },
            statusCode: status
        );
}
