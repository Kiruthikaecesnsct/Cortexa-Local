using Collector.Server.Application.Handlers;
using Collector.Server.Application.Upload;
using Collector.Server.Application.Upload.Validation;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Collector.Server.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddCollectorApplication(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<UploadOptions>()
            .Bind(configuration.GetSection(UploadOptions.SectionName))
            .ValidateOnStart();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<UploadOptions>, UploadOptionsValidator>());
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<KnowledgeUploadValidator>();
        services.AddScoped<UploadPreparer>();
        services.AddScoped<WriteKnowledgeBatchHandler>();
        services.AddScoped<SubmitKnowledgeUploadHandler>();
        return services;
    }
}
