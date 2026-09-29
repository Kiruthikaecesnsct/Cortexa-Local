using Cortexa.ModelRouter.Application.DTOs;

namespace Cortexa.ModelRouter.Application.Interfaces;

public interface IModelProvider
{
    Task<ModelResult> CompleteAsync(ModelRequest request, CancellationToken ct = default);
    IAsyncEnumerable<ModelChunk> StreamAsync(ModelRequest request, CancellationToken ct = default);
}
