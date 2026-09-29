using Cortexa.ModelRouter.Domain.ValueObjects;

namespace Cortexa.ModelRouter.Application.DTOs;

public sealed record ModelChunk(string ContentDelta, bool IsFinal, TokenUsage? Usage = null);
