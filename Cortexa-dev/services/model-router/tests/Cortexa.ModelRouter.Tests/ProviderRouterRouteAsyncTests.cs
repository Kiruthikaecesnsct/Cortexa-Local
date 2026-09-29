using Cortexa.ModelRouter.Application.Configuration;
using Cortexa.ModelRouter.Application.DTOs;
using Cortexa.ModelRouter.Application.Interfaces;
using Cortexa.ModelRouter.Application.Services;
using Cortexa.ModelRouter.Domain.ValueObjects;
using Cortexa.ModelRouter.Infrastructure.Configuration;
using Cortexa.ModelRouter.Infrastructure.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Cortexa.ModelRouter.Tests;

public sealed class ProviderRouterRouteAsyncTests
{
    private const string FoundryProviderName = "Foundry";
    private const string AnthropicProviderName = "Anthropic";
    private const string FoundryProviderKey = "azure-foundry";
    private const string AnthropicProviderKey = "anthropic";
    private static readonly TokenUsage SampleUsage = new(10, 20, 30);
    private static readonly ModelOptions DefaultOptions = new();

    private static CompleteRequest BuildRequest(string? model = null) =>
        new("analysis", "What is X?", null, null, model);

    private static ModelResult BuildModelResult(string provider) =>
        new(provider, "test-model", "response text", new[] { "cite-1" }, SampleUsage);

    private static (IModelProvider foundry, IModelProvider anthropic, ProviderRouter router) BuildRouter(string mode)
    {
        var foundry = Substitute.For<IModelProvider>();
        var anthropic = Substitute.For<IModelProvider>();
        var catalog = Substitute.For<IModelCatalog>();
        var settings = new RouterSettings { Mode = mode };
        var router = new ProviderRouter(
            foundry, anthropic, catalog, new GroundingValidator(), settings, NullLogger<ProviderRouter>.Instance);
        return (foundry, anthropic, router);
    }

    [Fact]
    public async Task RouteAsync_SinglePrimaryMode_CallsFoundryProviderAndReturnsGroundedResponse()
    {
        var (foundry, anthropic, router) = BuildRouter("single-foundry");
        var expectedResult = BuildModelResult(FoundryProviderName);
        foundry.CompleteAsync(Arg.Any<ModelRequest>(), Arg.Any<CancellationToken>())
               .Returns(expectedResult);

        var response = await router.RouteAsync(BuildRequest());

        await foundry.Received(1).CompleteAsync(Arg.Any<ModelRequest>(), Arg.Any<CancellationToken>());
        await anthropic.DidNotReceive().CompleteAsync(Arg.Any<ModelRequest>(), Arg.Any<CancellationToken>());
        response.Provider.Should().Be(FoundryProviderName);
        response.Grounding.Should().NotBeNull();
        response.Grounding!.IsGrounded.Should().BeTrue();
    }

    [Fact]
    public async Task RouteAsync_SingleSecondaryMode_CallsAnthropicProviderAndReturnsGroundedResponse()
    {
        var (foundry, anthropic, router) = BuildRouter("single-anthropic");
        var expectedResult = BuildModelResult(AnthropicProviderName);
        anthropic.CompleteAsync(Arg.Any<ModelRequest>(), Arg.Any<CancellationToken>())
                 .Returns(expectedResult);

        var response = await router.RouteAsync(BuildRequest());

        await anthropic.Received(1).CompleteAsync(Arg.Any<ModelRequest>(), Arg.Any<CancellationToken>());
        await foundry.DidNotReceive().CompleteAsync(Arg.Any<ModelRequest>(), Arg.Any<CancellationToken>());
        response.Provider.Should().Be(AnthropicProviderName);
        response.Grounding.Should().NotBeNull();
        response.Grounding!.IsGrounded.Should().BeTrue();
    }

    [Fact]
    public async Task RouteAsync_DualAdversarialMode_ThrowsInvalidOperationException()
    {
        var (_, _, router) = BuildRouter("dual");

        var act = async () => await router.RouteAsync(BuildRequest());

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task RouteAsync_ExplicitModelResolvesToFoundry_CallsFoundryProviderWithResolvedDeployment()
    {
        const string RequestedModelId = "gpt-5.4";
        const string ResolvedDeployment = "gpt-5-4-eastus-deployment";
        var (foundry, anthropic, catalog, router) = BuildRouterWithCatalog("single-foundry");
        catalog.Resolve(RequestedModelId).Returns(new ModelResolution(FoundryProviderKey, ResolvedDeployment, true));
        foundry.CompleteAsync(Arg.Any<ModelRequest>(), Arg.Any<CancellationToken>())
               .Returns(BuildModelResult(FoundryProviderName));

        var response = await router.RouteAsync(BuildRequest(RequestedModelId));

        await foundry.Received(1).CompleteAsync(
            Arg.Is<ModelRequest>(r => r.Model == ResolvedDeployment),
            Arg.Any<CancellationToken>());
        await anthropic.DidNotReceive().CompleteAsync(Arg.Any<ModelRequest>(), Arg.Any<CancellationToken>());
        response.Provider.Should().Be(FoundryProviderName);
    }

    [Fact]
    public async Task RouteAsync_ExplicitModelResolvesToAnthropic_CallsAnthropicProviderWithResolvedDeployment()
    {
        const string RequestedModelId = "claude-opus-4-8";
        const string ResolvedDeployment = "claude-opus-4-8-deployment";
        var (foundry, anthropic, catalog, router) = BuildRouterWithCatalog("single-foundry");
        catalog.Resolve(RequestedModelId).Returns(new ModelResolution(AnthropicProviderKey, ResolvedDeployment, true));
        anthropic.CompleteAsync(Arg.Any<ModelRequest>(), Arg.Any<CancellationToken>())
                 .Returns(BuildModelResult(AnthropicProviderName));

        var response = await router.RouteAsync(BuildRequest(RequestedModelId));

        await anthropic.Received(1).CompleteAsync(
            Arg.Is<ModelRequest>(r => r.Model == ResolvedDeployment),
            Arg.Any<CancellationToken>());
        await foundry.DidNotReceive().CompleteAsync(Arg.Any<ModelRequest>(), Arg.Any<CancellationToken>());
        response.Provider.Should().Be(AnthropicProviderName);
    }

    [Fact]
    public async Task RouteAsync_ExplicitModelUnknown_ThrowsInvalidOperationException()
    {
        const string RequestedModelId = "unknown-model";
        var (_, _, catalog, router) = BuildRouterWithCatalog("single-foundry");
        catalog.Resolve(RequestedModelId).Returns((ModelResolution?)null);

        var act = async () => await router.RouteAsync(BuildRequest(RequestedModelId));

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task RouteAsync_ExplicitModelDisabled_ThrowsInvalidOperationException()
    {
        const string RequestedModelId = "gpt-5.4";
        var (_, _, catalog, router) = BuildRouterWithCatalog("single-foundry");
        catalog.Resolve(RequestedModelId).Returns(new ModelResolution(FoundryProviderKey, "gpt-5-4-deployment", false));

        var act = async () => await router.RouteAsync(BuildRequest(RequestedModelId));

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task RouteAsync_ExplicitModelWithDualMode_DoesNotThrowAndRoutesSuccessfully()
    {
        const string RequestedModelId = "gpt-5.4";
        const string ResolvedDeployment = "gpt-5-4-eastus-deployment";
        var (foundry, _, catalog, router) = BuildRouterWithCatalog("dual");
        catalog.Resolve(RequestedModelId).Returns(new ModelResolution(FoundryProviderKey, ResolvedDeployment, true));
        foundry.CompleteAsync(Arg.Any<ModelRequest>(), Arg.Any<CancellationToken>())
               .Returns(BuildModelResult(FoundryProviderName));

        var response = await router.RouteAsync(BuildRequest(RequestedModelId));

        response.Provider.Should().Be(FoundryProviderName);
    }

    [Fact]
    public async Task RouteAsync_TaskKindProvided_PropagatesOriginalTaskKindToProviderRequest()
    {
        const string ExpectedTaskKind = "patent_research";
        var (foundry, anthropic, router) = BuildRouter("single-foundry");
        foundry.CompleteAsync(Arg.Any<ModelRequest>(), Arg.Any<CancellationToken>())
               .Returns(BuildModelResult(FoundryProviderName));

        await router.RouteAsync(new CompleteRequest(ExpectedTaskKind, "What is X?", null, null, null));

        await foundry.Received(1).CompleteAsync(
            Arg.Is<ModelRequest>(r => r.TaskKind == ExpectedTaskKind),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RouteAsync_NoModelRequested_ModelRequestCarriesNullModel()
    {
        var (foundry, _, _, router) = BuildRouterWithCatalog("single-foundry");
        foundry.CompleteAsync(Arg.Any<ModelRequest>(), Arg.Any<CancellationToken>())
               .Returns(BuildModelResult(FoundryProviderName));

        await router.RouteAsync(BuildRequest());

        await foundry.Received(1).CompleteAsync(
            Arg.Is<ModelRequest>(r => r.Model == null),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RouteAsync_GptAliasWithRealCatalog_ResolvesAndCallsFoundryProvider()
    {
        var settings = new ModelCatalogSettings
        {
            Models = new List<ModelCatalogEntry>
            {
                new()
                {
                    Id = "gpt-5.5",
                    Label = "GPT 5.5",
                    Provider = FoundryProviderKey,
                    Role = "primary",
                    Enabled = true,
                    Capabilities = new List<string> { "reasoning" }
                }
            },
            SingleDefault = "gpt-5.5",
            Aliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["gpt"] = "gpt-5.5"
            }
        };
        var realCatalog = new ModelCatalog(settings);
        var foundry = Substitute.For<IModelProvider>();
        var anthropic = Substitute.For<IModelProvider>();
        var routerSettings = new RouterSettings { Mode = "single-foundry" };
        var router = new ProviderRouter(
            foundry, anthropic, realCatalog, new GroundingValidator(), routerSettings, NullLogger<ProviderRouter>.Instance);
        foundry.CompleteAsync(Arg.Any<ModelRequest>(), Arg.Any<CancellationToken>())
               .Returns(BuildModelResult(FoundryProviderName));

        var response = await router.RouteAsync(BuildRequest("gpt"));

        await foundry.Received(1).CompleteAsync(
            Arg.Is<ModelRequest>(r => r.Model == "gpt-5.5"),
            Arg.Any<CancellationToken>());
        await anthropic.DidNotReceive().CompleteAsync(Arg.Any<ModelRequest>(), Arg.Any<CancellationToken>());
        response.Provider.Should().Be(FoundryProviderName);
    }

    private static (IModelProvider foundry, IModelProvider anthropic, IModelCatalog catalog, ProviderRouter router) BuildRouterWithCatalog(string mode)
    {
        var foundry = Substitute.For<IModelProvider>();
        var anthropic = Substitute.For<IModelProvider>();
        var catalog = Substitute.For<IModelCatalog>();
        var settings = new RouterSettings { Mode = mode };
        var router = new ProviderRouter(
            foundry, anthropic, catalog, new GroundingValidator(), settings, NullLogger<ProviderRouter>.Instance);
        return (foundry, anthropic, catalog, router);
    }
}
