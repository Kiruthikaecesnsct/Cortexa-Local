using Collector.Server.Api.Auth;
using Collector.Server.Application.Handlers;
using Collector.Server.Application.Upload;
using Microsoft.Extensions.Options;

namespace Collector.Server.Api.Endpoints;

public static class KnowledgeUploadEndpoints
{
    private const string GroupPath = "/collector/batches";
    private const string KnowledgePath = "/knowledge";

    public static IEndpointRouteBuilder MapKnowledgeUploadEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup(GroupPath)
            .RequireAuthorization(CollectorPolicies.JobsSubmit)
            .AddEndpointFilter<SecurityStampFilter>();
        group.MapPost(KnowledgePath, SubmitAsync);
        return app;
    }

    private static async Task<IResult> SubmitAsync(
        HttpContext http,
        SubmitKnowledgeUploadHandler handler,
        IOptions<UploadOptions> options,
        CancellationToken cancellationToken)
    {
        var input = await UploadRequestReader.ReadAsync(http, options.Value, cancellationToken);
        if (input.Command is null)
        {
            return input.Failure!;
        }

        var outcome = await handler.HandleAsync(input.Command, cancellationToken);
        return ToResult(outcome);
    }

    private static IResult ToResult(UploadOutcome outcome)
    {
        if (outcome.Result is null)
        {
            return ErrorResponses.Unprocessable(outcome.Errors);
        }

        return outcome.IsReplay
            ? Results.Ok(outcome.Result)
            : Results.Created($"{GroupPath}/{outcome.Result.BatchId}", outcome.Result);
    }
}
