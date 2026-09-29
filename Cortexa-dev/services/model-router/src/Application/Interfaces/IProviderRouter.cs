using Cortexa.ModelRouter.Application.DTOs;

namespace Cortexa.ModelRouter.Application.Interfaces;

public interface IProviderRouter
{
    Task<CompleteResponse> RouteAsync(CompleteRequest request, CancellationToken ct = default);
    Task<DualCompleteResponse> RouteDualAsync(CompleteRequest request, CancellationToken ct = default);
}
