using Cortexa.ModelRouter.Application.DTOs;
using Cortexa.ModelRouter.Application.Interfaces;

namespace Cortexa.ModelRouter.Application.Services;

public sealed class GroundingValidator : IGroundingValidator
{
    public GroundingResult Validate(ModelResult result)
    {
        if (result.Citations is { Count: > 0 })
            return new GroundingResult(true, 1f, string.Empty);

        return new GroundingResult(false, 0f, "No evidence citations returned by model");
    }
}
