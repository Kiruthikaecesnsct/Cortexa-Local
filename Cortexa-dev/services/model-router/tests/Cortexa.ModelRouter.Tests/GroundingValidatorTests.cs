using Cortexa.ModelRouter.Application.DTOs;
using Cortexa.ModelRouter.Application.Services;
using Cortexa.ModelRouter.Domain.ValueObjects;
using FluentAssertions;

namespace Cortexa.ModelRouter.Tests;

public sealed class GroundingValidatorTests
{
    private readonly GroundingValidator _sut = new();

    private static ModelResult BuildResult(IReadOnlyList<string>? citations) =>
        new("TestProvider", "test-model", "content", citations, new TokenUsage(10, 20, 30));

    [Fact]
    public void Validate_WithNonEmptyCitations_ReturnsGroundedTrue()
    {
        var result = BuildResult(new[] { "cite-1", "cite-2" });

        var grounding = _sut.Validate(result);

        grounding.IsGrounded.Should().BeTrue();
        grounding.Confidence.Should().Be(1f);
        grounding.Reason.Should().BeEmpty();
    }

    [Fact]
    public void Validate_WithNullCitations_ReturnsGroundedFalse()
    {
        var result = BuildResult(citations: null);

        var grounding = _sut.Validate(result);

        grounding.IsGrounded.Should().BeFalse();
        grounding.Confidence.Should().Be(0f);
        grounding.Reason.Should().Be("No evidence citations returned by model");
    }

    [Fact]
    public void Validate_WithEmptyCitationsList_ReturnsGroundedFalse()
    {
        var result = BuildResult(citations: Array.Empty<string>());

        var grounding = _sut.Validate(result);

        grounding.IsGrounded.Should().BeFalse();
        grounding.Confidence.Should().Be(0f);
    }
}
