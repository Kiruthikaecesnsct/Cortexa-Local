using Cortexa.ModelRouter.Domain.Enums;
using Cortexa.ModelRouter.Infrastructure.Configuration;
using FluentAssertions;

namespace Cortexa.ModelRouter.Tests;

public sealed class RouterSettingsTests
{
    [Theory]
    [InlineData("single-foundry")]
    [InlineData("unrecognised-value")]
    [InlineData("")]
    [InlineData("SINGLE-FOUNDRY")]
    public void ToModelMode_UnrecognisedOrSingleFoundry_ReturnsSinglePrimary(string mode)
    {
        var settings = new RouterSettings { Mode = mode };

        var result = settings.ToModelMode();

        result.Should().Be(ModelMode.SinglePrimary);
    }

    [Fact]
    public void ToModelMode_SingleAnthropic_ReturnsSingleSecondary()
    {
        var settings = new RouterSettings { Mode = "single-anthropic" };

        var result = settings.ToModelMode();

        result.Should().Be(ModelMode.SingleSecondary);
    }

    [Fact]
    public void ToModelMode_Dual_ReturnsDualAdversarial()
    {
        var settings = new RouterSettings { Mode = "dual" };

        var result = settings.ToModelMode();

        result.Should().Be(ModelMode.DualAdversarial);
    }

    [Fact]
    public void ToModelMode_DualUpperCase_ReturnsDualAdversarial()
    {
        var settings = new RouterSettings { Mode = "DUAL" };

        var result = settings.ToModelMode();

        result.Should().Be(ModelMode.DualAdversarial);
    }
}
