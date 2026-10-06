using Collector.Tests.Support;

namespace Collector.Tests.Auth;

public class TokenRefreshPolicyTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 6, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Skew = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan MinDelay = TimeSpan.FromSeconds(5);

    [Fact]
    public void Schedules_refresh_one_skew_before_expiry()
    {
        var expiresAt = Now.AddMinutes(15);

        var next = TestSupport.Policy().NextRefreshAt(expiresAt, Now);

        Assert.Equal(expiresAt - Skew, next);
    }

    [Fact]
    public void Applies_minimum_delay_floor_inside_the_skew_window()
    {
        var next = TestSupport.Policy().NextRefreshAt(Now.AddSeconds(10), Now);

        Assert.Equal(Now + MinDelay, next);
    }

    [Fact]
    public void Applies_minimum_delay_floor_when_already_expired()
    {
        var next = TestSupport.Policy().NextRefreshAt(Now.AddMinutes(-5), Now);

        Assert.Equal(Now + MinDelay, next);
    }

    [Fact]
    public void Reports_when_refresh_is_due()
    {
        var policy = TestSupport.Policy();

        Assert.False(policy.IsRefreshDue(Now.AddMinutes(5), Now));
        Assert.True(policy.IsRefreshDue(Now + Skew, Now));
        Assert.True(policy.IsRefreshDue(Now.AddMinutes(-1), Now));
    }
}
