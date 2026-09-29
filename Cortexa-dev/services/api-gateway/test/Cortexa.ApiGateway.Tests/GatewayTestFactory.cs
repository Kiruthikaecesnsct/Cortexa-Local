using Cortexa.ApiGateway.Api.Auth;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Cortexa.ApiGateway.Tests;

public sealed class GatewayTestFactory : WebApplicationFactory<Program>
{
    public FakeUserStatusClient UserStatusClient { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("UserStatus:IdentityInternalBaseUrl", "http://identity.test");

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IUserStatusClient>();
            services.AddSingleton<IUserStatusClient>(UserStatusClient);
        });
    }
}
