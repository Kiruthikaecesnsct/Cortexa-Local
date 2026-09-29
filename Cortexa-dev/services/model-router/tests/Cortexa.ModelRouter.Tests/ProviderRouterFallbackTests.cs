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

public sealed class ProviderRouterFallbackTests
{
    private const string FoundryProviderName = "Foundry";
    private const string SecondaryProviderName = "Anthropic";
    private const string FoundryProviderKey = "azure-foundry";
    private const string AnthropicProviderKey = "anthropic";
    private const string DeepSeekDeployment = "DeepSeek-V4-Pro";
    private static readonly TokenUsage SampleUsage = new(10, 20, 30);

    private static CompleteRequest BuildRequest() => new("analysis", "What is X?", null, null);

    private static ModelResult BuildModelResult(string provider) =>
        new(provider, "test-model", "response text", new[] { "cite-1" }, SampleUsage);

    private static (IModelProvider foundry, IModelProvider secondary, IModelCatalog catalog, ProviderRouter router) BuildRouter(
        bool enableFallback = true,
        bool hasSecondary = true)
    {
        var foundry = Substitute.For<IModelProvider>();
        var secondary = Substitute.For<IModelProvider>();
        var catalog = Substitute.For<IModelCatalog>();
        catalog.ResolveEnabledSecondary().Returns(
            hasSecondary ? new ModelResolution(AnthropicProviderKey, "claude-opus-4-8", true) : null);
        var settings = new RouterSettings { Mode = "single-foundry", EnableFallback = enableFallback };
        var router = new ProviderRouter(
            foundry, secondary, catalog, new GroundingValidator(), settings, NullLogger<ProviderRouter>.Instance);
        return (foundry, secondary, catalog, router);
    }

    private static (IModelProvider foundry, IModelProvider secondary, IModelCatalog catalog, ProviderRouter router) BuildRouterWithResolution(
        ModelResolution secondaryResolution,
        bool enableFallback = true)
    {
        var foundry = Substitute.For<IModelProvider>();
        var secondary = Substitute.For<IModelProvider>();
        var catalog = Substitute.For<IModelCatalog>();
        catalog.ResolveEnabledSecondary().Returns(secondaryResolution);
        var settings = new RouterSettings { Mode = "single-foundry", EnableFallback = enableFallback };
        var router = new ProviderRouter(
            foundry, secondary, catalog, new GroundingValidator(), settings, NullLogger<ProviderRouter>.Instance);
        return (foundry, secondary, catalog, router);
    }

    [Fact]
    public async Task RouteAsync_PrimaryThrowsModelProviderException_FallsBackToSecondaryAndReturnsItsResult()
    {
        var (foundry, secondary, catalog, router) = BuildRouter();
        foundry.CompleteAsync(Arg.Any<ModelRequest>(), Arg.Any<CancellationToken>())
               .ThrowsAsync(new ModelProviderException(FoundryProviderName, 500, "Foundry returned 500"));
        secondary.CompleteAsync(Arg.Any<ModelRequest>(), Arg.Any<CancellationToken>())
                 .Returns(BuildModelResult(SecondaryProviderName));

        var response = await router.RouteAsync(BuildRequest());

        response.Provider.Should().Be(SecondaryProviderName);
        await secondary.Received(1).CompleteAsync(Arg.Any<ModelRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RouteAsync_PrimaryThrowsFoundryTimeoutException_FallsBackToSecondaryAndReturnsItsResult()
    {
        var (foundry, secondary, catalog, router) = BuildRouter();
        foundry.CompleteAsync(Arg.Any<ModelRequest>(), Arg.Any<CancellationToken>())
               .ThrowsAsync(new FoundryTimeoutException("gpt-5.5", 80));
        secondary.CompleteAsync(Arg.Any<ModelRequest>(), Arg.Any<CancellationToken>())
                 .Returns(BuildModelResult(SecondaryProviderName));

        var response = await router.RouteAsync(BuildRequest());

        response.Provider.Should().Be(SecondaryProviderName);
        await secondary.Received(1).CompleteAsync(Arg.Any<ModelRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RouteAsync_BothProvidersFail_ThrowsAllProvidersFailedExceptionWithBothErrors()
    {
        var (foundry, secondary, catalog, router) = BuildRouter();
        var primaryError = new ModelProviderException(FoundryProviderName, 500, "Foundry returned 500");
        var secondaryError = new ModelProviderException(SecondaryProviderName, 503, "Secondary returned 503");
        foundry.CompleteAsync(Arg.Any<ModelRequest>(), Arg.Any<CancellationToken>()).ThrowsAsync(primaryError);
        secondary.CompleteAsync(Arg.Any<ModelRequest>(), Arg.Any<CancellationToken>()).ThrowsAsync(secondaryError);

        var act = async () => await router.RouteAsync(BuildRequest());

        var exception = await act.Should().ThrowAsync<AllProvidersFailedException>();
        exception.Which.PrimaryProvider.Should().Be(FoundryProviderName);
        exception.Which.SecondaryProvider.Should().Be(AnthropicProviderKey);
        exception.Which.PrimaryError.Should().BeSameAs(primaryError);
        exception.Which.SecondaryError.Should().BeSameAs(secondaryError);
    }

    [Fact]
    public async Task RouteAsync_PrimaryThrowsInvalidOperationException_DoesNotFallBackAndRethrows()
    {
        var (foundry, secondary, catalog, router) = BuildRouter();
        foundry.CompleteAsync(Arg.Any<ModelRequest>(), Arg.Any<CancellationToken>())
               .ThrowsAsync(new InvalidOperationException("Unknown or disabled model"));

        var act = async () => await router.RouteAsync(BuildRequest());

        await act.Should().ThrowAsync<InvalidOperationException>();
        await secondary.DidNotReceive().CompleteAsync(Arg.Any<ModelRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RouteAsync_CallerCancelledOperationCanceledException_DoesNotFallBackAndRethrows()
    {
        var (foundry, secondary, catalog, router) = BuildRouter();
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        foundry.CompleteAsync(Arg.Any<ModelRequest>(), Arg.Any<CancellationToken>())
               .ThrowsAsync(new OperationCanceledException(cts.Token));

        var act = async () => await router.RouteAsync(BuildRequest(), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        await secondary.DidNotReceive().CompleteAsync(Arg.Any<ModelRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RouteAsync_EnableFallbackFalse_DoesNotFallBackOnRetryableFailure()
    {
        var (foundry, secondary, catalog, router) = BuildRouter(enableFallback: false);
        foundry.CompleteAsync(Arg.Any<ModelRequest>(), Arg.Any<CancellationToken>())
               .ThrowsAsync(new ModelProviderException(FoundryProviderName, 500, "Foundry returned 500"));

        var act = async () => await router.RouteAsync(BuildRequest());

        await act.Should().ThrowAsync<ModelProviderException>();
        await secondary.DidNotReceive().CompleteAsync(Arg.Any<ModelRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RouteAsync_PrimaryThrowsModelProviderException400_DoesNotFallBackAndRethrows()
    {
        var (foundry, secondary, catalog, router) = BuildRouter();
        foundry.CompleteAsync(Arg.Any<ModelRequest>(), Arg.Any<CancellationToken>())
               .ThrowsAsync(new ModelProviderException(FoundryProviderName, 400, "Foundry returned 400"));

        var act = async () => await router.RouteAsync(BuildRequest());

        await act.Should().ThrowAsync<ModelProviderException>();
        await secondary.DidNotReceive().CompleteAsync(Arg.Any<ModelRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RouteAsync_PrimaryThrowsModelProviderException404_DoesNotFallBackAndRethrows()
    {
        var (foundry, secondary, catalog, router) = BuildRouter();
        foundry.CompleteAsync(Arg.Any<ModelRequest>(), Arg.Any<CancellationToken>())
               .ThrowsAsync(new ModelProviderException(FoundryProviderName, 404, "Foundry returned 404"));

        var act = async () => await router.RouteAsync(BuildRequest());

        await act.Should().ThrowAsync<ModelProviderException>();
        await secondary.DidNotReceive().CompleteAsync(Arg.Any<ModelRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RouteAsync_NoEnabledSecondary_DoesNotFallBackAndPropagatesOriginalTimeout()
    {
        var (foundry, secondary, catalog, router) = BuildRouter(hasSecondary: false);
        var primaryError = new FoundryTimeoutException("gpt-5.5", 80);
        foundry.CompleteAsync(Arg.Any<ModelRequest>(), Arg.Any<CancellationToken>()).ThrowsAsync(primaryError);

        var act = async () => await router.RouteAsync(BuildRequest());

        var exception = await act.Should().ThrowAsync<FoundryTimeoutException>();
        exception.Which.Should().BeSameAs(primaryError);
        await secondary.DidNotReceive().CompleteAsync(Arg.Any<ModelRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RouteAsync_NoEnabledSecondary_DoesNotFallBackOnRetryable5xx()
    {
        var (foundry, secondary, catalog, router) = BuildRouter(hasSecondary: false);
        foundry.CompleteAsync(Arg.Any<ModelRequest>(), Arg.Any<CancellationToken>())
               .ThrowsAsync(new ModelProviderException(FoundryProviderName, 500, "Foundry returned 500"));

        var act = async () => await router.RouteAsync(BuildRequest());

        await act.Should().ThrowAsync<ModelProviderException>();
        await secondary.DidNotReceive().CompleteAsync(Arg.Any<ModelRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RouteAsync_PrimaryThrowsModelProviderExceptionWithNullStatusCode_FallsBackToSecondary()
    {
        var (foundry, secondary, catalog, router) = BuildRouter();
        foundry.CompleteAsync(Arg.Any<ModelRequest>(), Arg.Any<CancellationToken>())
               .ThrowsAsync(new ModelProviderException(FoundryProviderName, null, "Foundry returned empty response"));
        secondary.CompleteAsync(Arg.Any<ModelRequest>(), Arg.Any<CancellationToken>())
                 .Returns(BuildModelResult(SecondaryProviderName));

        var response = await router.RouteAsync(BuildRequest());

        response.Provider.Should().Be(SecondaryProviderName);
        await secondary.Received(1).CompleteAsync(Arg.Any<ModelRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RouteAsync_SecondaryResolutionIsDeepSeekOnFoundry_FallsBackSuccessfully()
    {
        var (foundry, secondary, catalog, router) = BuildRouterWithResolution(
            new ModelResolution(FoundryProviderKey, DeepSeekDeployment, true));
        foundry.CompleteAsync(Arg.Any<ModelRequest>(), Arg.Any<CancellationToken>())
               .Returns(
                   _ => throw new ModelProviderException(FoundryProviderName, 500, "Foundry returned 500"),
                   _ => Task.FromResult(BuildModelResult(FoundryProviderName)));

        var response = await router.RouteAsync(BuildRequest());

        response.Provider.Should().Be(FoundryProviderName);
        await foundry.Received(2).CompleteAsync(Arg.Any<ModelRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RouteAsync_FailedModelMatchesSecondaryDeployment_DoesNotFallBack()
    {
        var (foundry, secondary, catalog, router) = BuildRouterWithResolution(
            new ModelResolution(AnthropicProviderKey, "claude-opus-4-8", true));
        catalog.Resolve("claude-opus-4-8").Returns(new ModelResolution(AnthropicProviderKey, "claude-opus-4-8", true));
        var request = new CompleteRequest("analysis", "What is X?", null, null, Model: "claude-opus-4-8");
        secondary.CompleteAsync(Arg.Any<ModelRequest>(), Arg.Any<CancellationToken>())
               .ThrowsAsync(new ModelProviderException(SecondaryProviderName, 500, "Secondary returned 500"));

        var act = async () => await router.RouteAsync(request);

        await act.Should().ThrowAsync<ModelProviderException>();
        await secondary.Received(1).CompleteAsync(Arg.Any<ModelRequest>(), Arg.Any<CancellationToken>());
    }
}
