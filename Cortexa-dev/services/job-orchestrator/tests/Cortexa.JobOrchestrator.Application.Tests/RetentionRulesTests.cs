using Cortexa.JobOrchestrator.Domain.Retention;
using FluentAssertions;
using Xunit;

namespace Cortexa.JobOrchestrator.Application.Tests;

public sealed class RetentionRulesTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 30, 12, 0, 0, TimeSpan.Zero);

    private const int RetentionDays = 30;
    private const int StuckHours = 72;

    [Fact]
    public void IsFailedExpired_FailedAtExactlyAtCutoff_ReturnsTrue()
    {
        var failedAt = Now.AddDays(-RetentionDays);

        var result = RetentionRules.IsFailedExpired(failedAt, null, Now, RetentionDays);

        result.Should().BeTrue();
    }

    [Fact]
    public void IsFailedExpired_FailedAtOneSecondNewerThanCutoff_ReturnsFalse()
    {
        var failedAt = Now.AddDays(-RetentionDays).AddSeconds(1);

        var result = RetentionRules.IsFailedExpired(failedAt, null, Now, RetentionDays);

        result.Should().BeFalse();
    }

    [Fact]
    public void IsFailedExpired_NullFailedAt_FallsBackToCreatedAt()
    {
        var createdAt = Now.AddDays(-RetentionDays - 1);

        var result = RetentionRules.IsFailedExpired(null, createdAt, Now, RetentionDays);

        result.Should().BeTrue();
    }

    [Fact]
    public void IsFailedExpired_BothNullDates_ReturnsFalse()
    {
        var result = RetentionRules.IsFailedExpired(null, null, Now, RetentionDays);

        result.Should().BeFalse();
    }

    [Fact]
    public void IsIncompleteStuck_CreatedAtOlderThanStuckHours_ReturnsTrue()
    {
        var createdAt = Now.AddHours(-StuckHours - 1);
        var lastActivity = Now.AddHours(-1);

        var result = RetentionRules.IsIncompleteStuck(createdAt, lastActivity, Now, StuckHours);

        result.Should().BeTrue();
    }

    [Fact]
    public void IsIncompleteStuck_CreatedAtOneSecondNewerThanCutoff_ReturnsFalse()
    {
        var createdAt = Now.AddHours(-StuckHours).AddSeconds(1);
        var lastActivity = Now.AddHours(-1);

        var result = RetentionRules.IsIncompleteStuck(createdAt, lastActivity, Now, StuckHours);

        result.Should().BeFalse();
    }

    [Fact]
    public void IsIncompleteStuck_NullCreatedAt_UsesLastActivityUtc()
    {
        var lastActivity = Now.AddHours(-StuckHours - 1);

        var result = RetentionRules.IsIncompleteStuck(null, lastActivity, Now, StuckHours);

        result.Should().BeTrue();
    }

    [Fact]
    public void IsWedgedRunning_ForceDisabled_AlwaysReturnsFalse()
    {
        var lastActivity = Now.AddHours(-StuckHours - 100);

        var result = RetentionRules.IsWedgedRunning(lastActivity, Now, StuckHours, forceEnabled: false);

        result.Should().BeFalse();
    }

    [Fact]
    public void IsWedgedRunning_ForceEnabledAndActivityBeyondThreshold_ReturnsTrue()
    {
        var lastActivity = Now.AddHours(-StuckHours - 1);

        var result = RetentionRules.IsWedgedRunning(lastActivity, Now, StuckHours, forceEnabled: true);

        result.Should().BeTrue();
    }

    [Fact]
    public void IsWedgedRunning_ForceEnabledButActivityRecent_ReturnsFalse()
    {
        var lastActivity = Now.AddHours(-StuckHours).AddSeconds(1);

        var result = RetentionRules.IsWedgedRunning(lastActivity, Now, StuckHours, forceEnabled: true);

        result.Should().BeFalse();
    }

    [Fact]
    public void IsFailedExpired_FailedAtWellBeyondCutoff_ReturnsTrue()
    {
        var failedAt = Now.AddDays(-RetentionDays - 10);

        var result = RetentionRules.IsFailedExpired(failedAt, null, Now, RetentionDays);

        result.Should().BeTrue();
    }

    [Fact]
    public void IsIncompleteStuck_CreatedAtExactlyAtCutoff_ReturnsTrue()
    {
        var createdAt = Now.AddHours(-StuckHours);
        var lastActivity = Now.AddHours(-1);

        var result = RetentionRules.IsIncompleteStuck(createdAt, lastActivity, Now, StuckHours);

        result.Should().BeTrue();
    }
}
