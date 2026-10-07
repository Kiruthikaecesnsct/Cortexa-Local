using Collector.Server.Api.Auth;
using Collector.Server.Application.Handlers;
using Collector.Server.Application.Upload;

namespace Collector.Server.Api.Endpoints;

public static class CollectorBatchesEndpoints
{
    private const string GroupPath = "/collector/batches";
    private const string ResultsPath = "/{id}/results";
    private const string BatchNotFoundError = "batch_not_found";
    private const string BatchNotFoundMessage = "No batch was found for this caller with that id.";

    public static IEndpointRouteBuilder MapCollectorBatchesEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup(GroupPath)
            .RequireAuthorization(CollectorPolicies.JobsSubmit)
            .AddEndpointFilter<SecurityStampFilter>();

        group.MapGet(string.Empty, ListAsync);
        group.MapGet(ResultsPath, GetResultsAsync);
        return app;
    }

    private static async Task<IResult> ListAsync(
        HttpContext http,
        ListBatchesHandler handler,
        CancellationToken cancellationToken)
    {
        if (!CollectorClaims.TryGetCaller(http.User, out var caller))
        {
            return Results.Unauthorized();
        }

        var batches = await handler.HandleAsync(caller, cancellationToken);
        return Results.Ok(batches);
    }

    private static async Task<IResult> GetResultsAsync(
        HttpContext http,
        string id,
        GetBatchResultsHandler handler,
        CancellationToken cancellationToken)
    {
        if (!CollectorClaims.TryGetCaller(http.User, out var caller))
        {
            return Results.Unauthorized();
        }

        var outcome = await handler.HandleAsync(id, caller, cancellationToken);
        return outcome.Found
            ? Results.Ok(outcome.Results)
            : ErrorResponses.NotFound(BatchNotFoundError, BatchNotFoundMessage);
    }
}
