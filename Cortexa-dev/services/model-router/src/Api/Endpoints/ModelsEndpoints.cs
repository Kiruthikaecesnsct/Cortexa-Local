using Cortexa.ModelRouter.Application.Interfaces;

namespace Cortexa.ModelRouter.Api.Endpoints;

public static class ModelsEndpoints
{
    public static IEndpointRouteBuilder MapModelsEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/models", GetModels);
        return app;
    }

    private static IResult GetModels(IModelCatalog catalog)
    {
        var response = catalog.Get();
        return Results.Ok(response);
    }
}
