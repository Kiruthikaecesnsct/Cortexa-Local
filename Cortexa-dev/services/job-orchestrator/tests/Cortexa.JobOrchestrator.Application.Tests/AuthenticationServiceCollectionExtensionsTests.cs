using Cortexa.JobOrchestrator.Api.Auth;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Cortexa.JobOrchestrator.Application.Tests;

public sealed class AuthenticationServiceCollectionExtensionsTests
{
    private const string ExpectedRoleClaimType = "role";

    private static IConfiguration BuildConfiguration()
    {
        var settings = new Dictionary<string, string?>
        {
            ["Jwt:Issuer"] = "https://identity.cortexa.test",
            ["Jwt:Audience"] = "cortexa-job-orchestrator",
            ["Jwt:SigningKey"] = "unit-test-signing-key-with-sufficient-length"
        };

        return new ConfigurationBuilder()
            .AddInMemoryCollection(settings)
            .Build();
    }

    private static JwtBearerOptions ResolveJwtBearerOptions(IConfiguration configuration)
    {
        var services = new ServiceCollection();
        services.AddOrchestratorAuthentication(configuration);

        using var provider = services.BuildServiceProvider();
        var optionsMonitor = provider.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>();

        return optionsMonitor.Get(JwtBearerDefaults.AuthenticationScheme);
    }

    [Fact]
    public void AddOrchestratorAuthentication_JwtBearerOptions_DisablesInboundClaimMapping()
    {
        var configuration = BuildConfiguration();

        var options = ResolveJwtBearerOptions(configuration);

        options.MapInboundClaims.Should().BeFalse();
    }

    [Fact]
    public void AddOrchestratorAuthentication_JwtBearerOptions_UsesRoleClaimType()
    {
        var configuration = BuildConfiguration();

        var options = ResolveJwtBearerOptions(configuration);

        options.TokenValidationParameters.RoleClaimType.Should().Be(ExpectedRoleClaimType);
    }

    [Fact]
    public void AddOrchestratorAuthentication_MissingSigningKey_ThrowsInvalidOperationException()
    {
        var settings = new Dictionary<string, string?>
        {
            ["Jwt:Issuer"] = "https://identity.cortexa.test",
            ["Jwt:Audience"] = "cortexa-job-orchestrator"
        };
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(settings)
            .Build();
        var services = new ServiceCollection();

        var act = () => services.AddOrchestratorAuthentication(configuration);

        act.Should().Throw<InvalidOperationException>();
    }
}
