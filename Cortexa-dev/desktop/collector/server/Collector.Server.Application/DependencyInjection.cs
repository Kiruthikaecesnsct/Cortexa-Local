using Collector.Server.Application.Handlers;
using Microsoft.Extensions.DependencyInjection;

namespace Collector.Server.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddCollectorApplication(this IServiceCollection services)
    {
        services.AddScoped<WriteKnowledgeBatchHandler>();
        return services;
    }
}
