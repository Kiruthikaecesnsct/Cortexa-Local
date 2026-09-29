namespace Cortexa.ModelRouter.Application.DTOs;

public sealed record DualCompleteResponse(CompleteResponse Primary, CompleteResponse Secondary);
