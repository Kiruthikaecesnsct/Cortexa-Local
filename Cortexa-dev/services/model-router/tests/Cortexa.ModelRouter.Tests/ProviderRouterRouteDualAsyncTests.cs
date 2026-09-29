using Cortexa.ModelRouter.Application.DTOs;
using Cortexa.ModelRouter.Application.Exceptions;
using Cortexa.ModelRouter.Application.Interfaces;
using Cortexa.ModelRouter.Application.Services;
using Cortexa.ModelRouter.Domain.ValueObjects;
using Cortexa.ModelRouter.Infrastructure.Configuration;
using Cortexa.ModelRouter.Infrastructure.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Cortexa.ModelRouter.Tests;

public sealed class ProviderRouterRouteDualAsyncTests
{
    private const string FoundryProviderName = "Foundry";
    private const string AnthropicProviderName = "Anthropic";
    private static readonly TokenUsage SampleUsage = new(10, 20, 30);

    private const string FoundryProviderKey = "azure-foundry";
    private const string AnthropicProviderKey = "anthropic";
    private const string DeepSeekDeployment = "DeepSeek-V4-Pro";

    private static CompleteRequest BuildRequest() =>
        new("analysis", "What is X?", null, null);

    private static ModelResult BuildModelResult(string provider) =>
        new(provider, "test-model", "response text", new[] { "cite-1" }, SampleUsage);

    private static (IModelProvider foundry, IModelProvider anthropic, IModelCatalog catalog, ProviderRouter router) BuildDualRouter()
    {
        var foundry = Substitute.For<IModelProvider>();
        var anthropic = Substitute.For<IModelProvider>();
        var catalog = Substitute.For<IModelCatalog>();
        var settings = new RouterSettings { Mode = "dual" };
        var router = new ProviderRouter(
            foundry, anthropic, catalog, new GroundingValidator(), settings, NullLogger<ProviderRouter>.Instance);
        return (foundry, anthropic, catalog, router);
    }

    [Fact]
    public async Task RouteDualAsync_EnabledSecondaryIsAnthropic_ReturnsPrimaryFromFoundryAndSecondaryFromAnthropic()
    {
        var (foundry, anthropic, catalog, router) = BuildDualRouter();
        catalog.ResolveEnabledSecondary().Returns(new ModelResolution(AnthropicProviderKey, "claude-opus-4-8", true));
        foundry.CompleteAsync(Arg.Any<ModelRequest>(), Arg.Any<CancellationToken>())
               .Returns(BuildModelResult(FoundryProviderName));
        anthropic.CompleteAsync(Arg.Any<ModelRequest>(), Arg.Any<CancellationToken>())
                 .Returns(BuildModelResult(AnthropicProviderName));

        var response = await router.RouteDualAsync(BuildRequest());

        response.Primary.Provider.Should().Be(FoundryProviderName);
        response.Secondary.Provider.Should().Be(AnthropicProviderName);
        response.Primary.Error.Should().BeNull();
        response.Secondary.Error.Should().BeNull();
    }

    [Fact]
    public async Task RouteDualAsync_FoundryFailsAnthropicSecondarySucceeds_PrimaryHasErrorSecondaryIsValid()
    {
        var (foundry, anthropic, catalog, router) = BuildDualRouter();
        catalog.ResolveEnabledSecondary().Returns(new ModelResolution(AnthropicProviderKey, "claude-opus-4-8", true));
        foundry.CompleteAsync(Arg.Any<ModelRequest>(), Arg.Any<CancellationToken>())
               .ThrowsAsync(new HttpRequestException("Foundry unavailable"));
        anthropic.CompleteAsync(Arg.Any<ModelRequest>(), Arg.Any<CancellationToken>())
                 .Returns(BuildModelResult(AnthropicProviderName));

        var response = await router.RouteDualAsync(BuildRequest());

        response.Primary.Error.Should().NotBeNull();
        response.Secondary.Provider.Should().Be(AnthropicProviderName);
        response.Secondary.Error.Should().BeNull();
    }

    [Fact]
    public async Task RouteDualAsync_BothProvidersFail_ThrowsAggregateException()
    {
        var (foundry, anthropic, catalog, router) = BuildDualRouter();
        catalog.ResolveEnabledSecondary().Returns(new ModelResolution(AnthropicProviderKey, "claude-opus-4-8", true));
        foundry.CompleteAsync(Arg.Any<ModelRequest>(), Arg.Any<CancellationToken>())
               .ThrowsAsync(new HttpRequestException("Foundry unavailable"));
        anthropic.CompleteAsync(Arg.Any<ModelRequest>(), Arg.Any<CancellationToken>())
                 .ThrowsAsync(new HttpRequestException("Anthropic unavailable"));

        var act = async () => await router.RouteDualAsync(BuildRequest());

        await act.Should().ThrowAsync<AggregateException>();
    }

    [Fact]
    public async Task RouteDualAsync_EnabledSecondaryIsFoundryDeployment_RoutesSecondaryThroughFoundryWithDeployment()
    {
        var (foundry, anthropic, catalog, router) = BuildDualRouter();
        catalog.ResolveEnabledSecondary().Returns(new ModelResolution(FoundryProviderKey, DeepSeekDeployment, true));
        foundry.CompleteAsync(
                Arg.Is<ModelRequest>(r => r.Model == null),
                Arg.Any<CancellationToken>())
            .Returns(BuildModelResult(FoundryProviderName));
        foundry.CompleteAsync(
                Arg.Is<ModelRequest>(r => r.Model == DeepSeekDeployment),
                Arg.Any<CancellationToken>())
            .Returns(new ModelResult(FoundryProviderName, DeepSeekDeployment, "secondary response", new[] { "cite-2" }, SampleUsage));

        var response = await router.RouteDualAsync(BuildRequest());

        response.Primary.Provider.Should().Be(FoundryProviderName);
        response.Secondary.Provider.Should().Be(FoundryProviderName);
        response.Secondary.Model.Should().Be(DeepSeekDeployment);
        response.Secondary.Error.Should().BeNull();
        await anthropic.DidNotReceive().CompleteAsync(Arg.Any<ModelRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RouteDualAsync_NoEnabledSecondary_ReturnsPrimaryWithReasonedSecondaryFailure_WithoutCallingAnthropic()
    {
        var (foundry, anthropic, catalog, router) = BuildDualRouter();
        catalog.ResolveEnabledSecondary().Returns((ModelResolution?)null);
        foundry.CompleteAsync(Arg.Any<ModelRequest>(), Arg.Any<CancellationToken>())
               .Returns(BuildModelResult(FoundryProviderName));

        var response = await router.RouteDualAsync(BuildRequest());

        response.Primary.Provider.Should().Be(FoundryProviderName);
        response.Primary.Error.Should().BeNull();
        response.Secondary.Error.Should().NotBeNullOrWhiteSpace();
        response.Secondary.Error.Should().NotContain("401");
        await anthropic.DidNotReceive().CompleteAsync(Arg.Any<ModelRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RouteDualAsync_NoEnabledSecondaryAndPrimaryFails_ThrowsAllProvidersFailedException()
    {
        var (foundry, anthropic, catalog, router) = BuildDualRouter();
        catalog.ResolveEnabledSecondary().Returns((ModelResolution?)null);
        foundry.CompleteAsync(Arg.Any<ModelRequest>(), Arg.Any<CancellationToken>())
               .ThrowsAsync(new HttpRequestException("Foundry unavailable"));

        var act = async () => await router.RouteDualAsync(BuildRequest());

        await act.Should().ThrowAsync<AllProvidersFailedException>();
        await anthropic.DidNotReceive().CompleteAsync(Arg.Any<ModelRequest>(), Arg.Any<CancellationToken>());
    }
}
