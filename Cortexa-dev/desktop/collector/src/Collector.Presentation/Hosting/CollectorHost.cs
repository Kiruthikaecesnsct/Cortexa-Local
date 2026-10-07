using System.IO;
using Collector.Application;
using Collector.Infrastructure;
using Collector.Infrastructure.Options;
using Collector.Presentation.Navigation;
using Collector.Presentation.ViewModels;
using Collector.Presentation.Views;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;

namespace Collector.Presentation.Hosting;

public static class CollectorHost
{
    private const string EnvironmentPrefix = "COLLECTOR_";
    private const string BaseConfigFile = "appsettings.json";
    private const string LocalConfigFile = "appsettings.local.json";
    private const string LogPathSuffix = ":Args:path";

    public static IHost Build()
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            ContentRootPath = AppContext.BaseDirectory,
            DisableDefaults = true,
        });
        ConfigureConfiguration(builder.Configuration);
        builder.Services.AddSerilog((_, logger) => logger.ReadFrom.Configuration(builder.Configuration));
        builder.Services.AddCollectorApplication();
        builder.Services.AddCollectorInfrastructure(builder.Configuration);
        builder.Services.AddCollectorPresentation();
        return builder.Build();
    }

    private static void ConfigureConfiguration(ConfigurationManager configuration)
    {
        configuration.SetBasePath(AppContext.BaseDirectory);
        configuration.AddJsonFile(BaseConfigFile, optional: false, reloadOnChange: false);
        configuration.AddJsonFile(LocalConfigFile, optional: true, reloadOnChange: false);
        AddUserSettings(configuration);
        configuration.AddEnvironmentVariables(EnvironmentPrefix);
        ExpandLogPaths(configuration);
    }

    private static void AddUserSettings(ConfigurationManager configuration)
    {
        var options = new UserSettingsOptions();
        configuration.GetSection(UserSettingsOptions.SectionName).Bind(options);
        var path = Environment.ExpandEnvironmentVariables(options.Path);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        configuration.AddJsonFile(path, optional: true, reloadOnChange: true);
    }

    private static void ExpandLogPaths(ConfigurationManager configuration)
    {
        var pathKeys = configuration.AsEnumerable()
            .Where(pair => pair.Key.StartsWith("Serilog:", StringComparison.Ordinal)
                && pair.Key.EndsWith(LogPathSuffix, StringComparison.Ordinal)
                && pair.Value is not null)
            .ToList();
        foreach (var (key, value) in pathKeys)
        {
            configuration[key] = Environment.ExpandEnvironmentVariables(value!);
        }
    }

    private static IServiceCollection AddCollectorPresentation(this IServiceCollection services)
    {
        services.AddScreens();
        services.AddSingleton<ShellViewModel>();
        services.AddSingleton<MainWindow>();
        return services;
    }
}
