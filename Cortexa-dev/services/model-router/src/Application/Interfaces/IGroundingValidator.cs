using Cortexa.ModelRouter.Application.DTOs;

namespace Cortexa.ModelRouter.Application.Interfaces;

public interface IGroundingValidator
{
    GroundingResult Validate(ModelResult result);
}
