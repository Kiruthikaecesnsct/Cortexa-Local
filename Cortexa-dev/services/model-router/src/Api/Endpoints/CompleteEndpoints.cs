using System.Globalization;
using Cortexa.ModelRouter.Application.DTOs;
using Cortexa.ModelRouter.Application.Exceptions;
using Cortexa.ModelRouter.Application.Interfaces;

namespace Cortexa.ModelRouter.Api.Endpoints;

public static class CompleteEndpoints
{
    private const int RetryAfterSeconds = 5;
    private const int ClientClosedRequestStatusCode = 499;

    public static IEndpointRouteBuilder MapCompleteEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/complete", CompleteAsync);
        app.MapPost("/complete/dual", CompleteDualAsync);
        return app;
    }

    internal static async Task<IResult> CompleteAsync(
        CompleteRequest request,
        IProviderRouter router,
        ILoggerFactory loggerFactory,
        HttpContext httpContext,
        CancellationToken ct)
    {
        var logger = loggerFactory.CreateLogger("ModelRouter.Complete");
        if (string.IsNullOrWhiteSpace(request.Prompt))
            return Results.Problem("Prompt must not be null or empty", statusCode: StatusCodes.Status422UnprocessableEntity);

        try
        {
            var response = await router.RouteAsync(request, ct);
            return Results.Ok(response);
        }
        catch (InvalidOperationException ex)
        {
            logger.LogWarning(
                "Rejected model request for {ModelId} on {Endpoint}: {Reason}",
                request.Model ?? "(unspecified)",
                "POST /complete",
                ex.Message);
            return Results.BadRequest(ex.Message);
        }
        catch (FoundryCapacityExceededException ex)
        {
            return HandleCapacityExceeded(ex, httpContext, logger, "POST /complete");
        }
        catch (FoundryTimeoutException ex)
        {
            return HandleFoundryTimeout(ex, httpContext, logger, "POST /complete");
        }
        catch (AllProvidersFailedException ex)
        {
            return HandleAllProvidersFailed(ex, httpContext, logger, "POST /complete");
        }
        catch (ModelProviderException ex)
        {
            return HandleModelProviderException(ex, httpContext, logger, "POST /complete");
        }
        catch (OperationCanceledException ex)
        {
            return HandleCancellation(ex, ct, logger, "POST /complete");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Upstream provider error on POST /complete");
            return Results.Problem("An upstream provider error occurred", statusCode: StatusCodes.Status502BadGateway);
        }
    }

    internal static async Task<IResult> CompleteDualAsync(
        CompleteRequest request,
        IProviderRouter router,
        ILoggerFactory loggerFactory,
        HttpContext httpContext,
        CancellationToken ct)
    {
        var logger = loggerFactory.CreateLogger("ModelRouter.CompleteDual");
        if (string.IsNullOrWhiteSpace(request.Prompt))
            return Results.Problem("Prompt must not be null or empty", statusCode: StatusCodes.Status422UnprocessableEntity);

        try
        {
            var response = await router.RouteDualAsync(request, ct);
            return Results.Ok(response);
        }
        catch (FoundryCapacityExceededException ex)
        {
            return HandleCapacityExceeded(ex, httpContext, logger, "POST /complete/dual");
        }
        catch (FoundryTimeoutException ex)
        {
            return HandleFoundryTimeout(ex, httpContext, logger, "POST /complete/dual");
        }
        catch (ModelProviderException ex)
        {
            return HandleModelProviderException(ex, httpContext, logger, "POST /complete/dual");
        }
        catch (OperationCanceledException ex)
        {
            return HandleCancellation(ex, ct, logger, "POST /complete/dual");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Upstream provider error on POST /complete/dual");
            return Results.Problem("An upstream provider error occurred", statusCode: StatusCodes.Status502BadGateway);
        }
    }

    internal static IResult HandleCancellation(
        OperationCanceledException ex,
        CancellationToken ct,
        ILogger logger,
        string endpoint)
    {
        if (ct.IsCancellationRequested)
        {
            logger.LogInformation("Client aborted request on {Endpoint}", endpoint);
            return Results.StatusCode(ClientClosedRequestStatusCode);
        }
        logger.LogWarning("Upstream provider timed out on {Endpoint}", endpoint);
        return Results.Problem("Upstream provider timed out", statusCode: StatusCodes.Status504GatewayTimeout);
    }

    internal static IResult HandleCapacityExceeded(
        FoundryCapacityExceededException ex,
        HttpContext httpContext,
        ILogger logger,
        string endpoint)
    {
        logger.LogWarning(
            "Foundry deployment {Deployment} at capacity ({MaxInFlight} in-flight) on {Endpoint}",
            ex.Deployment,
            ex.MaxInFlight,
            endpoint);
        httpContext.Response.Headers.RetryAfter = RetryAfterSeconds.ToString(CultureInfo.InvariantCulture);
        return Results.Problem(ex.Message, statusCode: StatusCodes.Status429TooManyRequests);
    }

    internal static IResult HandleFoundryTimeout(
        FoundryTimeoutException ex,
        HttpContext httpContext,
        ILogger logger,
        string endpoint)
    {
        logger.LogWarning(
            "Foundry call to {Deployment} exceeded server timeout of {TimeoutSeconds}s on {Endpoint}",
            ex.Deployment,
            ex.TimeoutSeconds,
            endpoint);
        httpContext.Response.Headers.RetryAfter = RetryAfterSeconds.ToString(CultureInfo.InvariantCulture);
        return Results.Problem(ex.Message, statusCode: StatusCodes.Status503ServiceUnavailable);
    }

    internal static IResult HandleAllProvidersFailed(
        AllProvidersFailedException ex,
        HttpContext httpContext,
        ILogger logger,
        string endpoint)
    {
        logger.LogError(
            ex,
            "Both providers failed on {Endpoint} (trace {TraceId}): {PrimaryProvider} error: {PrimaryError}; {SecondaryProvider} error: {SecondaryError}",
            endpoint,
            httpContext.TraceIdentifier,
            ex.PrimaryProvider,
            ex.PrimaryError.Message,
            ex.SecondaryProvider,
            ex.SecondaryError.Message);
        return Results.Problem(
            "All model providers failed to complete the request",
            statusCode: StatusCodes.Status502BadGateway);
    }

    internal static IResult HandleModelProviderException(
        ModelProviderException ex,
        HttpContext httpContext,
        ILogger logger,
        string endpoint)
    {
        var statusCode = ex.HttpStatusCode;

        if (statusCode is null || statusCode >= 500)
        {
            logger.LogError(ex, "Upstream provider error on {Endpoint}", endpoint);
            return Results.Problem("An upstream provider error occurred", statusCode: StatusCodes.Status502BadGateway);
        }

        if (ex.ErrorCode == "content_filter" && ex.ContentFilterCategories is { Length: > 0 } categories)
            return HandleContentFilter(ex, categories, logger, endpoint, statusCode.Value);

        logger.LogWarning(
            "Provider {Provider} returned {StatusCode} on {Endpoint}: {Message}",
            ex.ProviderName,
            statusCode,
            endpoint,
            ex.Message);

        return Results.Problem(ex.Message, statusCode: statusCode.Value);
    }

    private static IResult HandleContentFilter(
        ModelProviderException ex,
        string[] categories,
        ILogger logger,
        string endpoint,
        int statusCode)
    {
        logger.LogWarning(
            "Content filter rejected prompt on {Endpoint} from {Provider}: categories {Categories}",
            endpoint,
            ex.ProviderName,
            string.Join(", ", categories));

        return Results.Problem(
            title: "Content filter",
            detail: $"The prompt was rejected by the content filter. Filtered categories: {string.Join(", ", categories)}",
            statusCode: statusCode,
            extensions: new Dictionary<string, object?>
            {
                ["code"] = "content_filter",
                ["categories"] = categories
            });
    }
}
