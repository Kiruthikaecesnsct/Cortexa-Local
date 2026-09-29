using Cortexa.ModelRouter.Application.DTOs;
using Cortexa.ModelRouter.Application.Exceptions;
using Cortexa.ModelRouter.Application.Interfaces;
using Cortexa.ModelRouter.Domain.Enums;
using Cortexa.ModelRouter.Infrastructure.Configuration;
using Microsoft.Extensions.Logging;

namespace Cortexa.ModelRouter.Infrastructure.Services;

public sealed class ProviderRouter : IProviderRouter
{
    private const string FoundryProviderKey = "azure-foundry";
    private const string AnthropicProviderKey = "anthropic";
    private const string FoundryDisplayName = "Foundry";
    private const string NoSecondaryDisplayName = "None";
    private const string NoEnabledSecondaryMessage = "No enabled secondary model is configured in the model catalog";

    private readonly IModelProvider _foundry;
    private readonly IModelProvider _anthropic;
    private readonly IModelCatalog _catalog;
    private readonly IGroundingValidator _grounding;
    private readonly RouterSettings _settings;
    private readonly ILogger<ProviderRouter> _logger;

    public ProviderRouter(
        IModelProvider foundry,
        IModelProvider anthropic,
        IModelCatalog catalog,
        IGroundingValidator grounding,
        RouterSettings settings,
        ILogger<ProviderRouter> logger)
    {
        _foundry = foundry;
        _anthropic = anthropic;
        _catalog = catalog;
        _grounding = grounding;
        _settings = settings;
        _logger = logger;
    }

    public async Task<CompleteResponse> RouteAsync(CompleteRequest request, CancellationToken ct = default)
    {
        var (provider, modelRequest) = string.IsNullOrWhiteSpace(request.Model)
            ? BuildModeBasedRequest(request)
            : BuildExplicitModelRequest(request);

        var result = await CompleteWithFallbackAsync(provider, modelRequest, ct);
        return WrapWithGrounding(result);
    }

    private async Task<ModelResult> CompleteWithFallbackAsync(
        IModelProvider provider, ModelRequest modelRequest, CancellationToken ct)
    {
        try
        {
            return await provider.CompleteAsync(modelRequest, ct);
        }
        catch (Exception ex) when (ShouldFallback(provider, modelRequest, ex, ct))
        {
            return await FallbackToSecondaryAsync(modelRequest, ex, ct);
        }
    }

    private bool ShouldFallback(IModelProvider provider, ModelRequest modelRequest, Exception ex, CancellationToken ct)
    {
        if (!_settings.EnableFallback || !ReferenceEquals(provider, _foundry))
            return false;

        var resolution = _catalog.ResolveEnabledSecondary();
        if (resolution is null)
            return false;

        if (modelRequest.Model is not null &&
            string.Equals(resolution.Deployment, modelRequest.Model, StringComparison.Ordinal))
            return false;

        return IsRetryableFailure(ex, ct);
    }

    private static bool IsRetryableFailure(Exception ex, CancellationToken ct) =>
        ex switch
        {
            InvalidOperationException => false,
            OperationCanceledException => !ct.IsCancellationRequested,
            ModelProviderException mpe => mpe.HttpStatusCode is null or >= 500,
            FoundryTimeoutException => true,
            _ => false
        };

    private async Task<ModelResult> FallbackToSecondaryAsync(
        ModelRequest modelRequest, Exception primaryError, CancellationToken ct)
    {
        var resolution = _catalog.ResolveEnabledSecondary();
        if (resolution is null)
            throw primaryError;

        var secondaryProvider = ResolveProvider(resolution.Provider);
        var secondaryRequest = modelRequest with { Model = resolution.Deployment };

        _logger.LogWarning(
            primaryError,
            "Foundry provider call failed for deployment {Deployment}; falling back to enabled secondary {SecondaryProvider}/{SecondaryDeployment}",
            modelRequest.Model ?? "(default deployment)",
            resolution.Provider,
            resolution.Deployment);

        try
        {
            return await secondaryProvider.CompleteAsync(secondaryRequest, ct);
        }
        catch (Exception secondaryError) when (secondaryError is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogError(
                secondaryError,
                "Secondary fallback also failed for deployment {Deployment}",
                resolution.Deployment);
            throw new AllProvidersFailedException(FoundryDisplayName, primaryError, resolution.Provider, secondaryError);
        }
    }

    private (IModelProvider Provider, ModelRequest Request) BuildModeBasedRequest(CompleteRequest request)
    {
        var mode = _settings.ToModelMode();
        if (mode == ModelMode.DualAdversarial)
            throw new InvalidOperationException("Use POST /complete/dual when MODEL_MODE=dual");

        var provider = mode == ModelMode.SingleSecondary
            ? (IModelProvider)_anthropic
            : _foundry;

        return (provider, BuildModelRequest(request, mode));
    }

    private (IModelProvider Provider, ModelRequest Request) BuildExplicitModelRequest(CompleteRequest request)
    {
        var resolution = _catalog.Resolve(request.Model!);
        if (resolution is null || !resolution.Enabled)
            throw new InvalidOperationException($"Unknown or disabled model: {request.Model}");

        var provider = ResolveProvider(resolution.Provider);
        var mode = resolution.Provider == AnthropicProviderKey
            ? ModelMode.SingleSecondary
            : ModelMode.SinglePrimary;

        return (provider, BuildModelRequest(request, mode, resolution.Deployment));
    }

    private IModelProvider ResolveProvider(string providerKey) => providerKey switch
    {
        FoundryProviderKey => _foundry,
        AnthropicProviderKey => _anthropic,
        _ => throw new InvalidOperationException($"Unsupported provider: {providerKey}")
    };

    public async Task<DualCompleteResponse> RouteDualAsync(CompleteRequest request, CancellationToken ct = default)
    {
        var primaryRequest = BuildModelRequest(request, ModelMode.DualAdversarial);
        var primaryTask = TryCompleteAsync(_foundry, primaryRequest, ct);

        var secondaryResolution = _catalog.ResolveEnabledSecondary();
        if (secondaryResolution is null)
            return await CompleteSingleSidedDualAsync(primaryTask);

        var secondaryProvider = ResolveProvider(secondaryResolution.Provider);
        var secondaryRequest = BuildModelRequest(request, ModelMode.DualAdversarial, secondaryResolution.Deployment);
        var secondaryTask = TryCompleteAsync(secondaryProvider, secondaryRequest, ct);

        await Task.WhenAll(primaryTask, secondaryTask);

        var (primaryResponse, primaryError) = primaryTask.Result;
        var (secondaryResponse, secondaryError) = secondaryTask.Result;

        if (primaryError is not null && secondaryError is not null)
            throw new AggregateException("Both providers failed", primaryError, secondaryError);

        return new DualCompleteResponse(
            primaryResponse ?? FailedResponse(FoundryDisplayName, DescribeFailure(primaryError)),
            secondaryResponse ?? FailedResponse(secondaryResolution.Provider, DescribeFailure(secondaryError)));
    }

    private async Task<DualCompleteResponse> CompleteSingleSidedDualAsync(
        Task<(CompleteResponse? Response, Exception? Error)> primaryTask)
    {
        var (primaryResponse, primaryError) = await primaryTask;
        if (primaryError is not null)
        {
            throw new AllProvidersFailedException(
                FoundryDisplayName,
                primaryError,
                NoSecondaryDisplayName,
                new InvalidOperationException(NoEnabledSecondaryMessage));
        }

        _logger.LogWarning(
            "Dual-mode scoring requested but no enabled secondary provider is configured in the model catalog; returning primary result only");

        return new DualCompleteResponse(
            primaryResponse!,
            FailedResponse(NoSecondaryDisplayName, NoEnabledSecondaryMessage));
    }

    private async Task<(CompleteResponse? Response, Exception? Error)> TryCompleteAsync(
        IModelProvider provider, ModelRequest request, CancellationToken ct)
    {
        try
        {
            var result = await provider.CompleteAsync(request, ct);
            return (WrapWithGrounding(result), null);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return (null, ex);
        }
    }

    private static ModelRequest BuildModelRequest(CompleteRequest req, ModelMode mode, string? model = null) =>
        new(mode, req.TaskKind, req.Prompt, req.EvidenceRefs, req.Options ?? new ModelOptions(), model);

    private CompleteResponse WrapWithGrounding(ModelResult result) =>
        new(result.Provider, result.Model, result.Content, result.Citations,
            result.Usage, _grounding.Validate(result), FinishReason: result.FinishReason);

    private static CompleteResponse FailedResponse(string provider, string reason) =>
        new(provider, string.Empty, string.Empty, null, null, null, reason);

    private static string DescribeFailure(Exception? error) =>
        error?.Message ?? "Provider call failed";
}
