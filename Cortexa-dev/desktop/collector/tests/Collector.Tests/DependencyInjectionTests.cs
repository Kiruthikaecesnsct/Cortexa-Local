using Collector.Application;
using Collector.Application.Auth;
using Collector.Application.Settings;
using Collector.Infrastructure;
using Collector.Infrastructure.Auth;
using Collector.Infrastructure.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Logging;

namespace Collector.Tests;

public class DependencyInjectionTests
{
    private static ServiceProvider Build()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Gateway:BaseUrl"] = "https://gateway.example",
                ["CollectorServer:BaseUrl"] = "https://server.example",
            })
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCollectorApplication();
        services.AddCollectorInfrastructure(configuration);
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
    }

    private static List<Type> Chain(IHttpMessageHandlerFactory factory, string name)
    {
        var types = new List<Type>();
        HttpMessageHandler? handler = factory.CreateHandler(name);
        while (handler is not null)
        {
            types.Add(handler.GetType());
            handler = (handler as DelegatingHandler)?.InnerHandler;
        }

        return types;
    }

    [Fact]
    public void Graph_resolves_and_session_interfaces_share_one_instance()
    {
        using var provider = Build();

        var signIn = provider.GetRequiredService<ISignInService>();

        Assert.Same(signIn, provider.GetRequiredService<ISessionState>());
        Assert.Same(signIn, provider.GetRequiredService<IAccessTokenProvider>());
        Assert.NotNull(provider.GetRequiredService<SettingsService>());
    }

    [Fact]
    public void Bearer_handler_is_only_on_the_collector_server_client()
    {
        using var provider = Build();
        var factory = provider.GetRequiredService<IHttpMessageHandlerFactory>();

        Assert.DoesNotContain(typeof(BearerTokenHandler), Chain(factory, HttpClientNames.CortexaAuth));
        Assert.Contains(typeof(BearerTokenHandler), Chain(factory, HttpClientNames.CollectorServer));
    }

    [Fact]
    public void Auth_client_disables_cookies()
    {
        using var provider = Build();
        var factory = provider.GetRequiredService<IHttpMessageHandlerFactory>();

        var primary = Chain(factory, HttpClientNames.CortexaAuth).Last();

        Assert.Equal(typeof(SocketsHttpHandler), primary);
    }
}
