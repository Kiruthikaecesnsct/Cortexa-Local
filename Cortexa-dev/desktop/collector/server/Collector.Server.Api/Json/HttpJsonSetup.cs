using Collector.Domain.Serialization;

namespace Collector.Server.Api.Json;

public static class HttpJsonSetup
{
    public static IServiceCollection AddCollectorHttpJson(this IServiceCollection services) =>
        services.ConfigureHttpJsonOptions(options =>
        {
            var source = CollectorJson.CreateOptions();
            options.SerializerOptions.PropertyNamingPolicy = source.PropertyNamingPolicy;
            options.SerializerOptions.DefaultIgnoreCondition = source.DefaultIgnoreCondition;
            options.SerializerOptions.PropertyNameCaseInsensitive = source.PropertyNameCaseInsensitive;
            foreach (var converter in source.Converters)
            {
                options.SerializerOptions.Converters.Add(converter);
            }
        });
}
