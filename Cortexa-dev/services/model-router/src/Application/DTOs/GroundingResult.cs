namespace Cortexa.ModelRouter.Application.DTOs;

public sealed record GroundingResult(bool IsGrounded, float Confidence, string Reason);
