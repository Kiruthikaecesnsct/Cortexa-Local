using Cortexa.JobOrchestrator.Application.Contracts;
using Cortexa.JobOrchestrator.Domain.Enums;

namespace Cortexa.JobOrchestrator.Application.Handlers;

public sealed record SagaRoute(DocumentState? TargetState, string? NextEvent);

public static class SagaEventRouter
{
    private static readonly IReadOnlyDictionary<string, SagaRoute> Routes =
        new Dictionary<string, SagaRoute>(StringComparer.OrdinalIgnoreCase);

    public static SagaRoute? Resolve(string eventType)
    {
        Routes.TryGetValue(eventType, out var route);
        return route;
    }
}
