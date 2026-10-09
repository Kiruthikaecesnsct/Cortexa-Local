using Collector.Application.Auth;
using Collector.Presentation.ViewModels;

namespace Collector.Tests.Presentation;

public sealed class SessionBadgeTests
{
    [Theory]
    [InlineData("sruthi.s@wisework.in", "SS")]
    [InlineData("alpha@example.com", "A")]
    [InlineData("jane_doe-smith@example.com", "JD")]
    [InlineData("x.@example.com", "X")]
    [InlineData("42.team@example.com", "4T")]
    public void SignedIn_InitialsComeFromEmailLocalPart(string email, string expected)
    {
        var badge = SessionBadge.For(SessionState.SignedIn, email, signingOut: false);

        Assert.Equal(expected, badge.Initials);
        Assert.True(badge.HasInitials);
    }

    [Fact]
    public void SignedIn_EmptyEmail_HasNoInitials()
    {
        var badge = SessionBadge.For(SessionState.SignedIn, string.Empty, signingOut: false);

        Assert.Equal(string.Empty, badge.Initials);
        Assert.False(badge.HasInitials);
    }

    [Theory]
    [InlineData(SessionState.SignedOut)]
    [InlineData(SessionState.Expired)]
    public void NotSignedIn_HasNoInitials(SessionState state)
    {
        var badge = SessionBadge.For(state, "alpha@example.com", signingOut: false);

        Assert.False(badge.HasInitials);
    }
}
